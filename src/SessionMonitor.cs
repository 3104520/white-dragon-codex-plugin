using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DragonQuota
{
    internal sealed class TurnSummary
    {
        public DateTimeOffset At;
        public bool Completed;
        public long? Tokens;
        public long? Output;
        public double? Seconds;
        public int Tools;
        public double? OutputRate { get { return Seconds > 0 ? Output / Seconds : null; } }
    }

    internal sealed class LocalUsage
    {
        public string Id;
        public string Model;
        public DateTimeOffset At;
        public long? ContextUsed;
        public long? ContextLimit;
        public long? TotalTokens;
        public long? InputTokens;
        public long? CacheRead;
        public long? Output;
        public long? Reasoning;
        public long? CacheWrite;
        public int Turns;
        public int Tools;
        public int Compactions;
        public bool TurnOpen;
        public TurnSummary LatestCompletion;
        public TurnSummary[] RecentTurns = new TurnSummary[0];
        public KeyValuePair<string, int>[] TopTools = new KeyValuePair<string, int>[0];
        public QuotaSample Quota;

        public double? ContextPercent { get { return ContextUsed.HasValue && ContextLimit > 0 ? 100.0 * ContextUsed / ContextLimit : null; } }
        public double? CachePercent { get { return CacheRead.HasValue && InputTokens > 0 ? 100.0 * CacheRead / InputTokens : null; } }
        public long? NewInput { get { return CacheRead.HasValue && InputTokens.HasValue ? Math.Max(0, InputTokens.Value - CacheRead.Value) : (long?)null; } }
        public bool IsWorking(DateTimeOffset now) { return TurnOpen && now - At < TimeSpan.FromMinutes(20); }

        public LocalUsage Snapshot() { return (LocalUsage)MemberwiseClone(); }
    }

    internal sealed class SessionSnapshot
    {
        public LocalUsage[] Recent = new LocalUsage[0];
        public QuotaSample Quota;

        public LocalUsage Select(string id)
        {
            if (String.IsNullOrEmpty(id)) return Recent.FirstOrDefault();
            return Recent.FirstOrDefault(item => item.Id == id);
        }
    }

    internal sealed class SessionMonitor
    {
        private readonly string _root;
        private readonly Dictionary<string, SessionFile> _readers = new Dictionary<string, SessionFile>(StringComparer.OrdinalIgnoreCase);

        public SessionMonitor(string root = null)
        {
            if (root == null)
            {
                string home = Environment.GetEnvironmentVariable("CODEX_HOME");
                if (String.IsNullOrWhiteSpace(home))
                    home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
                root = Path.Combine(home, "sessions");
            }
            _root = root;
        }

        public SessionSnapshot Read(string pinned)
        {
            if (!Directory.Exists(_root)) return new SessionSnapshot();
            try
            {
                var ordered = new DirectoryInfo(_root).EnumerateFiles("rollout-*.jsonl", SearchOption.AllDirectories)
                    .OrderByDescending(file => file.LastWriteTimeUtc).ToList();
                var candidates = ordered.Take(14).ToList();
                if (!String.IsNullOrEmpty(pinned) && !candidates.Any(file => IsSession(file, pinned)))
                {
                    var selected = ordered.FirstOrDefault(file => IsSession(file, pinned));
                    if (selected != null) candidates.Add(selected);
                }
                var activePaths = new HashSet<string>(candidates.Select(file => file.FullName), StringComparer.OrdinalIgnoreCase);
                foreach (var path in _readers.Keys.Where(path => !activePaths.Contains(path)).ToArray())
                    _readers.Remove(path);
                foreach (var file in candidates)
                {
                    SessionFile reader;
                    if (!_readers.TryGetValue(file.FullName, out reader) || file.Length < reader.Offset)
                    {
                        reader = new SessionFile(file.FullName);
                        _readers[file.FullName] = reader;
                    }
                    reader.Read(file.Length);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            var sessions = _readers.Values.Select(file => file.GetSnapshot()).Where(item => item != null)
                .OrderByDescending(item => item.At).GroupBy(item => item.Id)
                .Select(group => group.First()).ToArray();
            return new SessionSnapshot {
                Recent = sessions,
                Quota = sessions.Select(item => item.Quota).Where(item => item != null).OrderByDescending(item => item.At).FirstOrDefault()
            };
        }

        private static bool IsSession(FileInfo file, string id)
        {
            return Path.GetFileNameWithoutExtension(file.Name).EndsWith("-" + id, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class SessionFile
        {
            private const int LineLimit = 2 * 1024 * 1024;
            private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
            private readonly string _path;
            private readonly LocalUsage _usage = new LocalUsage();
            private readonly Queue<TurnSummary> _rounds = new Queue<TurnSummary>();
            private readonly Dictionary<string, int> _toolCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly HashSet<string> _calls = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _starts = new HashSet<string>(StringComparer.Ordinal);

            private bool _excluded;
            private string _turn;
            private DateTimeOffset _started;
            private long? _beforeTokens;
            private long? _beforeOutput;
            private long? _turnTokens;
            private long? _turnOutput;
            private int _beforeTools;
            private LocalUsage _snapshot;
            private bool _changed;
            public long Offset;

            public SessionFile(string path) { _path = path; }

            public void Read(long length)
            {
                if (_excluded || length <= Offset) return;
                try
                {
                    using (var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var line = new MemoryStream())
                    {
                        file.Position = Offset;
                        var buffer = new byte[65536];
                        long cursor = Offset;
                        bool clipped = false;
                        int count;
                        while (cursor < length && (count = file.Read(buffer, 0, (int)Math.Min(buffer.Length, length - cursor))) > 0)
                        {
                            int start = 0;
                            while (start < count)
                            {
                                int end = Array.IndexOf(buffer, (byte)10, start, count - start);
                                int segment = (end >= 0 ? end : count) - start;
                                if (!clipped && line.Length + segment <= LineLimit)
                                    line.Write(buffer, start, segment);
                                else if (!clipped)
                                {
                                    int available = Math.Min(segment, Math.Max(0, 256 - (int)line.Length));
                                    line.Write(buffer, start, available);
                                    clipped = true;
                                }
                                if (end < 0) break;

                                bool header = Offset == 0;
                                Offset = cursor + end + 1;
                                UseLine(line.GetBuffer(), (int)line.Length, header, clipped);
                                if (_excluded) return;
                                line.SetLength(0);
                                start = end + 1;
                                clipped = false;
                            }
                            cursor += count;
                        }
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                if (_changed)
                {
                    _snapshot = _usage.Snapshot();
                    _snapshot.RecentTurns = _rounds.Reverse().ToArray();
                    _snapshot.TopTools = _toolCounts.OrderByDescending(call => call.Value).ThenBy(call => call.Key).Take(5).ToArray();
                    _changed = false;
                }
            }

            public LocalUsage GetSnapshot() { return _snapshot; }

            private void UseLine(byte[] bytes, int count, bool header, bool clipped)
            {
                string prefix = Utf8.GetString(bytes, 0, Math.Min(count, 256));
                if (!header && !Relevant(prefix)) return;
                if (clipped)
                {
                    if (header) _excluded = true;
                    else if (HasType(prefix, "compacted"))
                    {
                        _usage.Compactions++;
                        _changed = true;
                    }
                    return;
                }
                string json = count <= 256 ? prefix : Utf8.GetString(bytes, 0, count);
                var entry = CodexUsage.Parse(json.TrimStart('\ufeff'));
                var payload = CodexUsage.GetMap(entry, "payload");
                string kind = CodexUsage.GetString(entry, "type");
                if (header)
                {
                    string source = CodexUsage.GetString(payload, "source");
                    _usage.Id = CodexUsage.GetString(payload, "id") ?? Path.GetFileNameWithoutExtension(_path);
                    _excluded = kind != "session_meta" || (source != "vscode" && source != "cli" && source != "appServer" && source != "app_server");
                    return;
                }
                DateTimeOffset at;
                if (!DateTimeOffset.TryParse(CodexUsage.GetString(entry, "timestamp"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out at)) return;
                if (kind == "compacted") _usage.Compactions++;
                else if (kind == "turn_context") _usage.Model = CodexUsage.GetString(payload, "model") ?? _usage.Model;
                else if (kind == "response_item") CountTool(payload);
                else if (kind == "token_usage_record") ReadTokenRecord(payload);
                else if (kind == "event_msg") ReadEvent(payload, at);
                else return;

                if (at > _usage.At) _usage.At = at;
                _changed = true;
            }

            private void ReadEvent(Dictionary<string, object> payload, DateTimeOffset at)
            {
                string kind = CodexUsage.GetString(payload, "type");
                switch (kind)
                {
                    case "task_started":
                    case "turn_started":
                        TurnStart(payload, at);
                        break;
                    case "task_complete":
                    case "turn_completed":
                        TurnEnd(payload, at, true);
                        break;
                    case "turn_aborted":
                        TurnEnd(payload, at, false);
                        break;
                    case "token_count":
                        var info = CodexUsage.GetMap(payload, "info");
                        var mostRecent = CodexUsage.GetMap(info, "last_token_usage");
                        _usage.ContextUsed = CodexUsage.GetInt(mostRecent, "total_tokens") ?? _usage.ContextUsed;
                        _usage.ContextLimit = CodexUsage.GetInt(info, "model_context_window") ?? _usage.ContextLimit;
                        ReadTotals(CodexUsage.GetMap(info, "total_token_usage"));
                        var quota = CodexUsage.GetMap(payload, "rate_limits");
                        var id = CodexUsage.GetString(quota, "limit_id");
                        if (id == null || String.Equals(id, "codex", StringComparison.OrdinalIgnoreCase))
                        {
                            var reading = CodexUsage.ReadQuota(quota, at, false);
                            if (reading != null && (_usage.Quota == null ||
                                (at >= _usage.Quota.At && !reading.SameWindows(_usage.Quota)))) _usage.Quota = reading;
                        }
                        break;
                }
            }

            private void ReadTokenRecord(Dictionary<string, object> payload)
            {
                _usage.ContextUsed = CodexUsage.GetInt(CodexUsage.GetMap(payload, "usage"), "total_tokens") ?? _usage.ContextUsed;
                ReadTotals(CodexUsage.GetMap(payload, "thread_token_usage"));
                if (!_usage.TurnOpen || CodexUsage.GetString(payload, "turn_id") != _turn) return;
                var turn = CodexUsage.GetMap(payload, "turn_token_usage");
                _turnTokens = CodexUsage.GetInt(turn, "total_tokens") ?? _turnTokens;
                _turnOutput = CodexUsage.GetInt(turn, "output_tokens") ?? _turnOutput;
            }

            private void ReadTotals(Dictionary<string, object> total)
            {
                _usage.TotalTokens = CodexUsage.GetInt(total, "total_tokens") ?? _usage.TotalTokens;
                _usage.InputTokens = CodexUsage.GetInt(total, "input_tokens") ?? _usage.InputTokens;
                _usage.CacheRead = CodexUsage.GetInt(total, "cached_input_tokens") ?? _usage.CacheRead;
                _usage.Output = CodexUsage.GetInt(total, "output_tokens") ?? _usage.Output;
                _usage.Reasoning = CodexUsage.GetInt(total, "reasoning_output_tokens") ?? _usage.Reasoning;
                _usage.CacheWrite = CodexUsage.GetInt(total, "cache_write_input_tokens") ?? _usage.CacheWrite;
            }

            private void TurnStart(Dictionary<string, object> payload, DateTimeOffset at)
            {
                _usage.ContextLimit = CodexUsage.GetInt(payload, "model_context_window") ?? _usage.ContextLimit;
                string turn = CodexUsage.GetString(payload, "turn_id") ?? at.ToString("O", CultureInfo.InvariantCulture);
                if (!_starts.Add(turn)) return;
                _usage.Turns++;
                _usage.TurnOpen = true;
                _turn = turn;
                _started = CodexUsage.ParseUnixTime(CodexUsage.GetInt(payload, "started_at")) ?? at;
                _beforeTokens = _usage.TotalTokens ?? (_usage.Turns == 1 ? (long?)0 : null);
                _beforeOutput = _usage.Output ?? (_usage.Turns == 1 ? (long?)0 : null);
                _beforeTools = _usage.Tools;
                _turnTokens = null;
                _turnOutput = null;
                _calls.Clear();
            }

            private void TurnEnd(Dictionary<string, object> payload, DateTimeOffset at, bool completed)
            {
                string turn = CodexUsage.GetString(payload, "turn_id");
                if (!_usage.TurnOpen || (turn != null && turn != _turn)) return;
                var end = CodexUsage.ParseUnixTime(CodexUsage.GetInt(payload, "completed_at")) ?? at;
                double elapsed = (end - _started).TotalSeconds;
                long? duration = CodexUsage.GetInt(payload, "duration_ms");
                var summary = new TurnSummary {
                    At = at, Completed = completed, Seconds = duration.HasValue ? duration.Value / 1000.0 : elapsed,
                    Tokens = _turnTokens ?? Difference(_usage.TotalTokens, _beforeTokens),
                    Output = _turnOutput ?? Difference(_usage.Output, _beforeOutput),
                    Tools = _usage.Tools - _beforeTools
                };
                _rounds.Enqueue(summary);
                if (_rounds.Count > 8) _rounds.Dequeue();
                if (completed) _usage.LatestCompletion = summary;
                _usage.TurnOpen = false;
            }

            private void CountTool(Dictionary<string, object> payload)
            {
                string kind = CodexUsage.GetString(payload, "type");
                if (kind != "function_call" && kind != "custom_tool_call" && kind != "web_search_call") return;
                string call = CodexUsage.GetString(payload, "call_id") ?? CodexUsage.GetString(payload, "id");
                if (call != null && !_calls.Add(call)) return;
                string name = CodexUsage.GetString(payload, "name") ?? "web_search";
                if (name.Length > 80) name = name.Substring(0, 80);
                int count;
                _toolCounts.TryGetValue(name, out count);
                _toolCounts[name] = count + 1;
                _usage.Tools++;
            }

            private static long? Difference(long? final, long? initial)
            {
                if (final.HasValue && initial.HasValue && final >= initial) return final.Value - initial.Value;
                return null;
            }

            private static bool Relevant(string prefix)
            {
                return HasType(prefix, "token_count") || HasType(prefix, "token_usage_record") ||
                    HasType(prefix, "task_started") || HasType(prefix, "task_complete") ||
                    HasType(prefix, "turn_started") || HasType(prefix, "turn_completed") ||
                    HasType(prefix, "turn_aborted") || HasType(prefix, "turn_context") ||
                    HasType(prefix, "compacted") || HasType(prefix, "function_call") ||
                    HasType(prefix, "custom_tool_call") || HasType(prefix, "web_search_call");
            }

            private static bool HasType(string prefix, string value)
            {
                return prefix.IndexOf("\"type\":\"" + value + "\"", StringComparison.Ordinal) >= 0 ||
                    prefix.IndexOf("\"type\": \"" + value + "\"", StringComparison.Ordinal) >= 0;
            }
        }
    }
}
