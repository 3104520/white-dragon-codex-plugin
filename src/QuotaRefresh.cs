using System;

namespace DragonQuota
{
    internal sealed class QuotaRefresh
    {
        private QuotaSample _service;
        private QuotaSample _session;
        private DateTimeOffset _serviceValueAt;
        private DateTimeOffset _lastAttempt;
        private DateTimeOffset _retryAfter;
        private int _failures;

        public bool Pending { get; private set; }

        public bool TryBegin(DateTimeOffset now, int intervalSeconds, bool immediate, bool manual)
        {
            var sample = Latest;
            immediate |= sample != null && (ResetPassed(sample.Short, now) || ResetPassed(sample.Long, now));
            if (Pending || (!manual && (now < _retryAfter ||
                now - _lastAttempt < TimeSpan.FromSeconds(immediate ? 4 : intervalSeconds)))) return false;
            _lastAttempt = now;
            Pending = true;
            return true;
        }

        public void Finish(QuotaSample sample, DateTimeOffset now)
        {
            Pending = false;
            if (sample != null)
            {
                if (!sample.SameWindows(_service)) _serviceValueAt = sample.At;
                _service = sample;
                _failures = 0;
                _retryAfter = DateTimeOffset.MinValue;
                return;
            }
            _failures = Math.Min(_failures + 1, 5);
            _retryAfter = now.AddSeconds(Math.Min(60, 5 << (_failures - 1)));
        }

        public bool Observe(QuotaSample sample, DateTimeOffset now)
        {
            if (sample == null || sample.At > now.AddSeconds(5) ||
                (_session != null && sample.At <= _session.At)) return false;
            _session = sample;
            return true;
        }

        public QuotaSample Current
        {
            get
            {
                var sample = Latest;
                return sample != null && sample == _session ? sample.WithOtherPools(_service) : sample;
            }
        }

        private QuotaSample Latest
        {
            get
            {
                if (_session == null) return _service;
                if (_service == null || _session.At > _service.At) return _session;
                // An unchanged response can lag the desktop's newly recorded quota.
                if (_session.At > _serviceValueAt && _service.At - _session.At <= TimeSpan.FromSeconds(45) &&
                    String.Equals(_service.Id, _session.Id, StringComparison.OrdinalIgnoreCase) && !_session.SameWindows(_service)) return _session;
                return _service;
            }
        }

        private bool ResetPassed(RateWindow window, DateTimeOffset now)
        {
            return window != null && window.ResetsAt.HasValue && window.ResetsAt.Value > _lastAttempt && window.ResetsAt.Value <= now;
        }

        public string Status(DateTimeOffset now, int intervalSeconds)
        {
            var sample = Latest;
            if (sample == null) return Pending ? "正在同步" : _failures > 0 ? "接口未连通" : "等待额度";
            var source = sample.FromService ? "接口" : "会话记录";
            if (Pending) source = "同步中";
            else if (sample.FromService && (_failures > 0 || IsOld(sample, now, intervalSeconds))) source = "接口缓存";
            else if (!sample.FromService && IsOld(sample, now, intervalSeconds)) source = "会话缓存";
            return source + " · " + Age(sample.At, now);
        }

        private static bool IsOld(QuotaSample sample, DateTimeOffset now, int intervalSeconds)
        {
            return now - sample.At >= TimeSpan.FromSeconds(Math.Max(30, intervalSeconds * 2));
        }

        internal static string Age(DateTimeOffset at, DateTimeOffset now)
        {
            var age = now - at;
            if (age.TotalSeconds < 4) return "刚刚";
            if (age.TotalMinutes < 1) return (int)age.TotalSeconds + " 秒前";
            if (age.TotalHours < 1) return (int)age.TotalMinutes + " 分钟前";
            if (age.TotalDays < 1) return (int)age.TotalHours + " 小时前";
            return (int)age.TotalDays + " 天前";
        }
    }
}
