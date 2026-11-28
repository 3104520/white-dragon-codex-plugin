using System;
using System.IO;
using System.Text;
using DragonQuota;

internal static class QuotaRefreshTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero);

    public static void Main(string[] args)
    {
        RefreshCadence();
        NewerSessionWins();
        FailedRequestBacksOff();
        CrossingResetRefreshes();
        DuplicateLogRecords(args[0]);
        Console.WriteLine("Passed quota cadence, source ordering, retry, reset and session-log tests.");
    }

    private static void RefreshCadence()
    {
        var quota = new QuotaRefresh();
        Check(quota.TryBegin(Start, 15, false, false), "first foreground refresh");
        Check(!quota.TryBegin(Start.AddSeconds(1), 15, true, true), "manual refresh shares in-flight request");
        quota.Finish(Reading(Start, 80, true), Start.AddSeconds(1));
        Check(!quota.TryBegin(Start.AddSeconds(14), 15, false, false), "foreground interval");
        Check(quota.TryBegin(Start.AddSeconds(15), 15, false, false), "scheduled refresh without popup");
    }

    private static void NewerSessionWins()
    {
        var quota = new QuotaRefresh();
        var initial = Reading(Start, 80, true);
        initial.OtherPools = new[] { new QuotaSample { Id = "model", At = Start, FromService = true } };
        quota.Finish(initial, Start.AddSeconds(1));
        Check(quota.Observe(Reading(Start.AddSeconds(3), 62, false), Start.AddSeconds(3)), "new session reading");
        Check(!quota.Observe(Reading(Start.AddSeconds(1), 90, false), Start.AddSeconds(4)), "no replay of old sample");
        Check(!quota.Observe(Reading(Start.AddHours(1), 50, false), Start.AddSeconds(4)), "ignore future sample");
        Check(quota.Current.Short.Remaining == 62 && quota.Current.OtherPools.Length == 1, "keep newer session and model buckets");
        quota.Finish(Reading(Start.AddSeconds(2), 80, true), Start.AddSeconds(5));
        Check(quota.Current.Short.Remaining == 62, "late response issued before session event");
        quota.Finish(Reading(Start.AddSeconds(10), 80, true), Start.AddSeconds(11));
        Check(quota.Current.Short.Remaining == 62, "repeat of earlier service values");
        quota.Finish(Reading(Start.AddSeconds(16), 61, true), Start.AddSeconds(17));
        Check(quota.Current.Short.Remaining == 61 && quota.Current.FromService, "accept changed service response");
    }

    private static void FailedRequestBacksOff()
    {
        var quota = new QuotaRefresh();
        quota.Finish(Reading(Start, 80, true), Start.AddSeconds(1));
        quota.TryBegin(Start.AddSeconds(15), 15, false, false);
        quota.Finish(null, Start.AddSeconds(16));
        Check(quota.Status(Start.AddSeconds(17), 15).Contains("缓存"), "offline source label");
        Check(!quota.TryBegin(Start.AddSeconds(20), 15, true, false), "no quick retry during backoff");
        Check(quota.TryBegin(Start.AddSeconds(20), 15, false, true), "manual retry bypasses backoff");
        quota.Finish(Reading(Start.AddSeconds(20), 75, true), Start.AddSeconds(21));
        Check(!quota.Status(Start.AddSeconds(21), 15).Contains("缓存"), "clear failure after recovery");
    }

    private static void CrossingResetRefreshes()
    {
        var quota = new QuotaRefresh();
        quota.TryBegin(Start, 60, false, false);
        var sample = Reading(Start, 30, true);
        sample.Short.ResetsAt = Start.AddSeconds(8);
        quota.Finish(sample, Start.AddSeconds(1));
        Check(!quota.TryBegin(Start.AddSeconds(7), 60, false, false), "no early refresh before reset");
        Check(quota.TryBegin(Start.AddSeconds(8), 60, false, false), "a reset advances refresh");
        sample.At = Start.AddSeconds(8);
        quota.Finish(sample, Start.AddSeconds(9));
        Check(!quota.TryBegin(Start.AddSeconds(13), 60, false, false), "a single refresh for a reset");
    }

    private static void DuplicateLogRecords(string directory)
    {
        var path = Path.Combine(directory, "rollout-quota-test.jsonl");
        File.WriteAllLines(path, new[] {
            "{\"type\":\"session_meta\",\"payload\":{\"id\":\"quota-test\",\"source\":\"vscode\"}}",
            Event(Start, 20), Event(Start.AddSeconds(5), 20)
        }, new UTF8Encoding(false));
        var monitor = new SessionMonitor(directory);
        Check(monitor.Read(null).Quota.At == Start, "repeated snapshot must retain original date");
        File.AppendAllText(path, Event(Start.AddSeconds(6), 31) + Environment.NewLine);
        var changed = monitor.Read(null).Quota;
        Check(changed.At == Start.AddSeconds(6) && changed.Short.Remaining == 69, "incremental session log change");
    }

    private static string Event(DateTimeOffset at, int used)
    {
        return "{\"timestamp\":\"" + at.ToString("o") + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\"," +
            "\"rate_limits\":{\"limit_id\":\"codex\",\"primary\":{\"used_percent\":" + used + ",\"window_minutes\":300}}}}";
    }

    private static QuotaSample Reading(DateTimeOffset at, double remaining, bool service)
    {
        return new QuotaSample { Id = "codex", At = at, FromService = service,
            Short = new RateWindow { Minutes = 300, Remaining = remaining } };
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
    }
}
