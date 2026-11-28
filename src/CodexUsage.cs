using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DragonQuota
{
    internal sealed class RateWindow
    {
        public double Remaining;
        public int Minutes;
        public DateTimeOffset? ResetsAt;
    }

    internal sealed class QuotaSample
    {
        public string Id;
        public string Name;
        public string Plan;
        public RateWindow Short;
        public RateWindow Long;
        public DateTimeOffset At;
        public bool FromService;
        public QuotaSample[] OtherPools = new QuotaSample[0];

        public QuotaSample WithOtherPools(QuotaSample service)
        {
            if (service == null) return this;
            var copy = (QuotaSample)MemberwiseClone();
            copy.OtherPools = service.OtherPools;
            if (String.Equals(Id, service.Id, StringComparison.OrdinalIgnoreCase)) copy.Plan = Plan ?? service.Plan;
            return copy;
        }

        public bool SameWindows(QuotaSample sample)
        {
            return sample != null && String.Equals(Id, sample.Id, StringComparison.OrdinalIgnoreCase) &&
                SameWindow(Short, sample.Short) && SameWindow(Long, sample.Long);
        }

        private static bool SameWindow(RateWindow a, RateWindow b)
        {
            return a == null || b == null ? a == b : a.Minutes == b.Minutes &&
                a.Remaining == b.Remaining && a.ResetsAt == b.ResetsAt;
        }
    }

    internal static class CodexUsage
    {
        private static readonly DateTimeOffset UnixEpoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static async Task<QuotaSample> ReadLiveAsync(string proxy)
        {
            string executable = FindCodex();
            if (executable == null) return null;

            var start = new ProcessStartInfo(executable, "app-server --listen stdio://") {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Utf8,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
            if (!String.IsNullOrEmpty(proxy))
            {
                start.EnvironmentVariables["HTTP_PROXY"] = proxy;
                start.EnvironmentVariables["HTTPS_PROXY"] = proxy;
            }

            using (var process = new Process { StartInfo = start })
            {
                bool started = false;
                try
                {
                    started = process.Start();
                    if (!started) return null;
                    process.BeginErrorReadLine();
                    Task timeout = Task.Delay(TimeSpan.FromSeconds(9));
                    string version = typeof(CodexUsage).Assembly.GetName().Version.ToString(3);
                    Send(process, "{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"dragon_quota\",\"title\":\"Dragon Quota\",\"version\":\"" + version + "\"}}}");
                    if (await ReadResult(process, 1, timeout).ConfigureAwait(false) == null) return null;
                    Send(process, "{\"method\":\"initialized\"}");
                    // A local quota event written during this request may be newer than its response.
                    var requestedAt = DateTimeOffset.Now;
                    Send(process, "{\"id\":2,\"method\":\"account/rateLimits/read\"}");
                    return ReadQuotaResult(await ReadResult(process, 2, timeout).ConfigureAwait(false), requestedAt);
                }
                catch (System.ComponentModel.Win32Exception) { return null; }
                catch (IOException) { return null; }
                catch (InvalidOperationException) { return null; }
                finally
                {
                    if (started)
                    {
                        try { process.StandardInput.Close(); } catch (IOException) { }
                        try { if (!process.WaitForExit(400)) process.Kill(); }
                        catch (InvalidOperationException) { }
                        catch (System.ComponentModel.Win32Exception) { }
                    }
                }
            }
        }

        internal static QuotaSample ReadQuotaResult(Dictionary<string, object> result, DateTimeOffset at)
        {
            var byId = GetMap(result, "rateLimitsByLimitId");
            var additional = new List<QuotaSample>();
            var main = ReadQuota(GetMap(result, "rateLimits"), at, true);
            if (byId != null)
            {
                foreach (var pool in byId)
                {
                    var sample = ReadQuota(pool.Value as Dictionary<string, object>, at, true, pool.Key);
                    if (sample == null) continue;
                    if (String.Equals(sample.Id, "codex", StringComparison.OrdinalIgnoreCase)) main = sample;
                    else additional.Add(sample);
                }
            }
            if (main == null && additional.Count > 0)
            {
                main = additional[0];
                additional.RemoveAt(0);
            }
            if (main != null)
                main.OtherPools = additional.FindAll(pool => !String.Equals(pool.Id, main.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
            return main;
        }

        internal static QuotaSample ReadQuota(Dictionary<string, object> page, DateTimeOffset at, bool live, string fallbackId = null)
        {
            var primary = ReadWindow(GetMap(page, "primary"), live);
            var secondary = ReadWindow(GetMap(page, "secondary"), live);
            if (primary == null && secondary == null) return null;
            if (primary != null && secondary != null && primary.Minutes > 0 &&
                secondary.Minutes > 0 && primary.Minutes > secondary.Minutes)
            {
                var earlier = secondary;
                secondary = primary;
                primary = earlier;
            }
            var id = GetString(page, live ? "limitId" : "limit_id") ?? fallbackId ?? "codex";
            return new QuotaSample {
                Id = id, Name = GetString(page, live ? "limitName" : "limit_name") ?? (id == "codex" ? "Codex" : id),
                Plan = GetString(page, live ? "planType" : "plan_type"),
                Short = primary, Long = secondary, At = at, FromService = live
            };
        }

        private static RateWindow ReadWindow(Dictionary<string, object> value, bool live)
        {
            var used = GetNumber(value, live ? "usedPercent" : "used_percent");
            if (!used.HasValue || Double.IsNaN(used.Value) || Double.IsInfinity(used.Value)) return null;
            var minutes = GetInt(value, live ? "windowDurationMins" : "window_minutes");
            var reset = GetInt(value, live ? "resetsAt" : "resets_at");
            var window = new RateWindow {
                Remaining = 100 - Math.Max(0, Math.Min(100, used.Value)),
                Minutes = (int)Math.Min(minutes ?? 0, Int32.MaxValue)
            };
            window.ResetsAt = ParseUnixTime(reset);
            return window;
        }

        internal static DateTimeOffset? ParseUnixTime(long? seconds)
        {
            if (seconds.HasValue && seconds.Value >= 0 && seconds.Value <= 253402300799L)
                return UnixEpoch.AddSeconds(seconds.Value);
            return null;
        }

        private static string FindCodex()
        {
            foreach (string executable in new[] { "ChatGPT", "Codex" })
            {
                string found = null;
                foreach (Process app in Process.GetProcessesByName(executable))
                {
                    using (app)
                    {
                        if (found != null) continue;
                        try
                        {
                            string folder = Path.GetDirectoryName(app.MainModule.FileName);
                            string bundled = Path.Combine(folder, "resources", "codex.exe");
                            if (File.Exists(bundled)) found = bundled;
                        }
                        catch (System.ComponentModel.Win32Exception) { }
                        catch (InvalidOperationException) { }
                    }
                }
                if (found != null) return found;
            }
            foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                string directory = Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));
                try
                {
                    if (!Path.IsPathRooted(directory)) continue;
                    string candidate = Path.Combine(directory, "codex.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
            }
            return null;
        }

        private static void Send(Process process, string json)
        {
            byte[] bytes = Utf8.GetBytes(json + "\n");
            var pipe = process.StandardInput.BaseStream;
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
        }

        private static async Task<Dictionary<string, object>> ReadResult(Process process, int id, Task timeout)
        {
            while (!timeout.IsCompleted)
            {
                Task<string> line = process.StandardOutput.ReadLineAsync();
                if (await Task.WhenAny(line, timeout).ConfigureAwait(false) != line) return null;
                string json = await line.ConfigureAwait(false);
                if (json == null) return null;
                var value = Parse(json);
                if (GetInt(value, "id") == id) return GetMap(value, "result");
            }
            return null;
        }

        internal static Dictionary<string, object> Parse(string json)
        {
            try { return new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        internal static Dictionary<string, object> GetMap(Dictionary<string, object> map, string key)
        {
            object value;
            return map != null && map.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        internal static string GetString(Dictionary<string, object> map, string key)
        {
            object value;
            return map != null && map.TryGetValue(key, out value) ? value as string : null;
        }

        internal static long? GetInt(Dictionary<string, object> map, string key)
        {
            object value;
            if (map != null && map.TryGetValue(key, out value))
            {
                if (value is int) return (int)value;
                if (value is long) return (long)value;
            }
            return null;
        }

        internal static object GetValue(Dictionary<string, object> map, string key)
        {
            object value;
            return map != null && map.TryGetValue(key, out value) ? value : null;
        }

        internal static double? GetNumber(Dictionary<string, object> map, string key)
        {
            object value;
            if (map == null || !map.TryGetValue(key, out value)) return null;
            if (value is int || value is long || value is decimal || value is double)
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return null;
        }
    }
}
