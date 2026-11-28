using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DragonQuota
{
    internal enum PanelPage { Summary, Context, Settings }

    internal sealed class TelemetryPanel : Window
    {
        public const double PanelWidth = 388;
        private readonly WidgetSettings _settings;
        private readonly DragonArt _art;
        private readonly Action _changed;
        private readonly Action _refetch;
        private readonly Action _layout;
        private readonly IridescentRim _rim = new IridescentRim();
        private readonly DockPanel _layoutRoot = new DockPanel();
        private readonly ScrollViewer _scroller;
        private readonly StackPanel _body = new StackPanel();
        private readonly DockPanel _toolbar = new DockPanel();
        private readonly StackPanel _footer = new StackPanel { Orientation = Orientation.Horizontal };
        private SessionSnapshot _sessions = new SessionSnapshot();
        private LocalUsage _session;
        private QuotaSample _quota;
        private string _quotaStatus;
        private string _copyMessage;
        private DateTimeOffset _copyUntil;
        private bool _loading;
        private DateTimeOffset _at;
        private Border _badge;
        private ComboBox _sessionPicker;
        private Button _refresh;
        private Button _copy;
        private SettingsView _preferences;
        private StackPanel _contextDetails;
        private string _choices;
        private bool _populating;

        public PanelPage Page { get; private set; }
        public double PanelHeight { get { return Page == PanelPage.Settings ? 684 : Page == PanelPage.Context ? 590 : 474; } }

        public TelemetryPanel(WidgetSettings settings, DragonArt art, Action changed, Action refetch, Action layout)
        {
            _settings = settings;
            _art = art;
            _changed = changed;
            _refetch = refetch;
            _layout = layout;
            _at = DateTimeOffset.Now;
            Title = "白龙额度详情";
            Width = PanelWidth;
            Height = 474;
            ShowInTaskbar = false;
            ShowActivated = true;
            Topmost = true;
            WindowStyle = WindowStyle.None;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            UseLayoutRounding = true;
            var paper = new LinearGradientBrush(Color.FromArgb(251, 255, 252, 247), Color.FromArgb(248, 248, 243, 254), 115);
            var liner = new Border {
                BorderBrush = WidgetView.Paint("#B5E8D4AC"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13),
                Margin = new Thickness(1), Padding = new Thickness(12, 9, 12, 10), Child = _layoutRoot
            };
            var shell = new Border {
                Background = paper, BorderBrush = _rim.Brush, BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(16), Child = liner
            };
            Content = shell;
            IsVisibleChanged += (sender, args) => _rim.SetVisible(IsVisible);
            _toolbar.Margin = new Thickness(0, 0, 0, 12);
            _footer.Margin = new Thickness(0, 12, 0, 0);
            DockPanel.SetDock(_toolbar, Dock.Top);
            DockPanel.SetDock(_footer, Dock.Bottom);
            _layoutRoot.Children.Add(_toolbar);
            _layoutRoot.Children.Add(_footer);
            _scroller = new ScrollViewer {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _body
            };
            var scrollStyle = new Style(typeof(System.Windows.Controls.Primitives.ScrollBar));
            scrollStyle.Setters.Add(new Setter(WidthProperty, 10.0));
            _scroller.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), scrollStyle);
            _layoutRoot.Children.Add(_scroller);
            PreviewKeyDown += (sender, args) => {
                if (args.Key != Key.Escape || (_sessionPicker != null && _sessionPicker.IsDropDownOpen)) return;
                Hide();
                args.Handled = true;
            };
            SetPage(PanelPage.Summary);
        }

        public void SetPage(PanelPage page)
        {
            Page = page;
            _sessionPicker = null;
            _choices = null;
            _refresh = null;
            _copy = null;
            _preferences = null;
            _contextDetails = null;
            _toolbar.Children.Clear();
            _footer.Children.Clear();
            _body.Children.Clear();

            var close = PanelTheme.Button("×", Hide);
            close.Width = 31;
            AutomationProperties.SetName(close, "关闭面板");
            DockPanel.SetDock(close, Dock.Right);
            _toolbar.Children.Add(close);
            if (page == PanelPage.Summary)
            {
                _copy = PanelTheme.Button("复制", CopyQuota);
                _copy.ToolTip = "复制额度摘要";
                AutomationProperties.SetName(_copy, "复制额度摘要");
                _copy.Margin = new Thickness(0, 0, 5, 0);
                DockPanel.SetDock(_copy, Dock.Right);
                _toolbar.Children.Add(_copy);
            }
            if (page != PanelPage.Summary)
            {
                var back = PanelTheme.Button("‹ 返回", () => SetPage(PanelPage.Summary));
                back.Margin = new Thickness(0, 0, 9, 0);
                DockPanel.SetDock(back, Dock.Left);
                _toolbar.Children.Add(back);
            }
            else
            {
                var avatar = new Image {
                    Source = _art.Get(DragonPose.Idle), Width = 31, Height = 31, Margin = new Thickness(0, 0, 7, 0)
                };
                RenderOptions.SetBitmapScalingMode(avatar, BitmapScalingMode.HighQuality);
                DockPanel.SetDock(avatar, Dock.Left);
                _toolbar.Children.Add(avatar);
            }
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(Heading(page == PanelPage.Settings ? "挂件设置" :
                page == PanelPage.Context ? "上下文观察" : "白龙额度", 17));
            _badge = new Border {
                Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(6, 3, 6, 3),
                Background = PanelTheme.Tint, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center,
                Visibility = page == PanelPage.Summary ? Visibility.Visible : Visibility.Collapsed
            };
            titleRow.Children.Add(_badge);
            _toolbar.Children.Add(titleRow);

            if (page == PanelPage.Summary)
            {
                AddFooter(PanelTheme.Button("上下文观察", () => SetPage(PanelPage.Context), true));
                AddRefresh();
                AddFooter(PanelTheme.Button("挂件设置", () => SetPage(PanelPage.Settings)));
            }
            else if (page == PanelPage.Context)
            {
                AddRefresh();
            }
            else
            {
                _preferences = new SettingsView(_settings, _art, _changed);
                _body.Children.Add(_preferences);
                AddFooter(PanelTheme.Button("完成", () => SetPage(PanelPage.Summary), true));
            }

            RenderData();
            _scroller.ScrollToTop();
            _layout();
        }

        public void UpdateData(SessionSnapshot sessions, LocalUsage session, QuotaSample quota, bool loading, DateTimeOffset now,
            string quotaStatus = null)
        {
            _sessions = sessions;
            _session = session;
            _quota = quota;
            _quotaStatus = quotaStatus;
            _loading = loading;
            _at = now;
            RenderData();
        }

        public void SyncCorner()
        {
            if (_preferences != null) _preferences.UpdateCorner();
        }

        private void RenderData()
        {
            if (_refresh != null)
            {
                _refresh.Content = _loading ? "刷新中…" : "刷新";
                _refresh.IsEnabled = !_loading;
            }
            if (_copy != null)
            {
                _copy.Content = _at < _copyUntil ? _copyMessage : "复制";
                _copy.IsEnabled = _quota != null;
            }
            if (Page == PanelPage.Summary) RenderSummary();
            if (Page == PanelPage.Context) RenderContext();
            if (_preferences != null) _preferences.UpdateBubblePreview(_quota, _session, _loading, _at, _quotaStatus);
        }

        private void RenderSummary()
        {
            _body.Children.Clear();
            bool busy = _session != null && _session.IsWorking(_at);
            _badge.Background = busy ? WidgetView.Paint("#EDE3FA") : PanelTheme.Tint;
            _badge.Child = PanelTheme.Text(busy ? "处理中" : _session == null ? "等待会话" : "待机中", 10.5, PanelTheme.Purple, true);
            if (_session != null)
            {
                var status = PanelTheme.Row(Muted(_settings.PinnedSession == null ? "最近 Codex 会话" : "已固定 Codex 会话"),
                    Muted(_session.Model ?? "模型待记录"));
                status.ToolTip = _session.Model;
                status.Margin = new Thickness(0, 0, 0, 10);
                _body.Children.Add(status);
            }
            AddContextMeter(_body);
            _body.Children.Add(PanelTheme.Rule());
            AddUsage(_body);
            _body.Children.Add(PanelTheme.Rule());
            AddQuotas(_body);
        }

        private void RenderContext()
        {
            if (_sessionPicker != null && _sessionPicker.IsDropDownOpen) return;
            if (_sessionPicker == null)
            {
                _sessionPicker = new ComboBox { FontFamily = PanelTheme.Font, FontSize = 12, Height = 30, Margin = new Thickness(0, 5, 0, 12) };
                AutomationProperties.SetName(_sessionPicker, "选择要观察的 Codex 会话");
                _sessionPicker.SelectionChanged += (sender, args) => {
                    var item = _sessionPicker.SelectedItem as SessionOption;
                    if (_populating || item == null) return;
                    _settings.PinnedSession = item.Id;
                    _session = _sessions.Select(item.Id);
                    _changed();
                    if (!_sessionPicker.IsDropDownOpen) RenderContext();
                };
                _sessionPicker.DropDownClosed += (sender, args) => RenderContext();
                _body.Children.Add(Heading("会话来源", 12));
                _body.Children.Add(_sessionPicker);
                _contextDetails = new StackPanel();
                _body.Children.Add(_contextDetails);
            }
            var options = new List<SessionOption> { new SessionOption(null, "自动 · 最近 Codex 会话") };
            options.AddRange(_sessions.Recent.Select(item => new SessionOption(item.Id,
                WidgetView.LocalTime(item.At, _at) + "  ·  " + (item.Model ?? "Codex") + "  ·  " + ShortId(item.Id))));
            if (_settings.PinnedSession != null && !options.Any(item => item.Id == _settings.PinnedSession))
                options.Add(new SessionOption(_settings.PinnedSession, "固定会话 · 暂无可读记录"));
            string signature = String.Join("\n", options.Select(item => item.Id + "|" + item.Label));
            if (_choices != signature)
            {
                _populating = true;
                _sessionPicker.ItemsSource = options;
                _sessionPicker.SelectedItem = options.First(item => item.Id == _settings.PinnedSession);
                _populating = false;
                _choices = signature;
            }
            _contextDetails.Children.Clear();
            AddContextMeter(_contextDetails);
            _contextDetails.Children.Add(PanelTheme.Rule());
            AddUsage(_contextDetails);
            if (_session == null) return;
            AddOptionalUsage("推理 Token · 计入输出", _session.Reasoning);
            AddOptionalUsage("缓存写入 Token", _session.CacheWrite);
            _contextDetails.Children.Add(PanelTheme.Rule());
            _contextDetails.Children.Add(Heading("最近轮次"));
            if (_session.RecentTurns.Length == 0) _contextDetails.Children.Add(Muted("尚无已结束的轮次。"));
            foreach (var turn in _session.RecentTurns)
            {
                string details = turn.Completed ? "+" + PanelTheme.Count(turn.Tokens) + " tok" : "已中断";
                if (turn.OutputRate.HasValue && turn.Completed) details += " · " + Rate(turn.OutputRate.Value);
                var row = PanelTheme.Row(Muted(WidgetView.LocalTime(turn.At, _at) + " · " + turn.Tools + " 次工具"), PanelTheme.Text(details, 11.5));
                row.Margin = new Thickness(0, 7, 0, 0);
                _contextDetails.Children.Add(row);
            }
            if (_session.TopTools.Length == 0) return;
            _contextDetails.Children.Add(PanelTheme.Rule());
            _contextDetails.Children.Add(Heading("常用工具"));
            foreach (var tool in _session.TopTools)
            {
                var row = PanelTheme.Row(Muted(tool.Key), PanelTheme.Text(tool.Value + " 次", 11.5));
                row.Margin = new Thickness(0, 6, 0, 0);
                _contextDetails.Children.Add(row);
            }
        }

        private void AddContextMeter(Panel panel)
        {
            double? usage = _session == null ? null : _session.ContextPercent;
            string label = usage.HasValue ? PanelTheme.Count(_session.ContextUsed) + " / " + PanelTheme.Count(_session.ContextLimit) +
                "  ·  " + PanelTheme.Percent(usage) : "等待上下文记录";
            Brush color = usage > 90 ? PanelTheme.Rose : usage > 75 ? PanelTheme.Amber : PanelTheme.Purple;
            panel.Children.Add(PanelTheme.Row(Heading("上下文长度"), PanelTheme.Text(label, 11.5, _rim.Accent(color), true, WidgetFonts.Mono)));
            panel.Children.Add(PanelTheme.Progress(usage, color));
            if (_session != null) panel.Children.Add(Muted("上下文已压缩 " + _session.Compactions + " 次"));
        }

        private void AddUsage(Panel panel)
        {
            string summary = _session == null ? "" : _session.Turns + " 轮  ·  " + _session.Tools + " 次工具";
            panel.Children.Add(PanelTheme.Row(Heading("本会话已用 Token"), Muted(summary)));
            var metrics = new Grid { Margin = new Thickness(0, 8, 0, 7) };
            metrics.ColumnDefinitions.Add(new ColumnDefinition());
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7) });
            metrics.ColumnDefinitions.Add(new ColumnDefinition());
            var cache = Metric("缓存命中率", PanelTheme.Percent(_session == null ? null : _session.CachePercent), PanelTheme.Green);
            Grid.SetColumn(cache, 2);
            metrics.Children.Add(Metric("累计 Token", PanelTheme.Count(_session == null ? null : _session.TotalTokens), PanelTheme.Ink));
            metrics.Children.Add(cache);
            panel.Children.Add(metrics);
            if (_session == null) return;
            var tokens = Muted("新输入 " + PanelTheme.Count(_session.NewInput) + "   ·   缓存读 " +
                PanelTheme.Count(_session.CacheRead) + "   ·   输出 " + PanelTheme.Count(_session.Output));
            tokens.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(tokens);
            var previous = _session.LatestCompletion;
            if (previous != null && previous.OutputRate.HasValue)
            {
                var speed = Muted("最近一轮平均输出 " + Rate(previous.OutputRate.Value));
                speed.Margin = new Thickness(0, 3, 0, 0);
                panel.Children.Add(speed);
            }
        }

        private void AddOptionalUsage(string label, long? count)
        {
            if (!count.HasValue) return;
            var row = PanelTheme.Row(Muted(label), PanelTheme.Text(PanelTheme.Count(count), 11.5));
            row.Margin = new Thickness(0, 6, 0, 0);
            _contextDetails.Children.Add(row);
        }

        private void AddQuotas(Panel panel)
        {
            string freshness = _quotaStatus ?? (_quota == null ? "" : (_quota.FromService ? "接口 · " : "会话记录 · ") + QuotaRefresh.Age(_quota.At, _at));
            panel.Children.Add(PanelTheme.Row(Heading("模型剩余额度"), Muted(freshness)));
            if (_quota == null)
            {
                var unavailable = Muted(_loading ? "正在查询 Codex 额度…" : "暂未取得 Codex 额度");
                unavailable.Margin = new Thickness(0, 9, 0, 0);
                panel.Children.Add(unavailable);
                return;
            }
            foreach (var pool in new[] { _quota }.Concat(_quota.OtherPools))
            {
                string detail = String.IsNullOrWhiteSpace(pool.Plan) ? "" : pool.Plan.ToUpperInvariant();
                if (pool.At != _quota.At) detail = "接口 · " + QuotaRefresh.Age(pool.At, _at);
                var name = PanelTheme.Row(Heading(pool.Name ?? "Codex", 12),
                    Muted(detail));
                name.Margin = new Thickness(0, 8, 0, 5);
                panel.Children.Add(name);
                AddRate(panel, pool.Short);
                AddRate(panel, pool.Long);
            }
        }

        private void CopyQuota()
        {
            if (_quota == null) return;
            var now = DateTimeOffset.Now;
            var lines = new List<string> { "Codex 额度", _quotaStatus ?? "记录于 " + WidgetView.LocalTime(_quota.At, now) };
            foreach (var pool in new[] { _quota }.Concat(_quota.OtherPools))
            {
                lines.Add((pool.Name ?? "Codex") + " · " + WidgetView.LocalTime(pool.At, now));
                foreach (var window in new[] { pool.Short, pool.Long })
                {
                    if (window == null) continue;
                    bool expired = window.ResetsAt.HasValue && window.ResetsAt.Value <= now;
                    var value = expired ? "等待同步" : "剩余 " + WidgetView.Percent(window.Remaining);
                    lines.Add(WidgetView.WindowName(window) + " · " + value +
                        (window.ResetsAt.HasValue ? " · 重置 " + window.ResetsAt.Value.LocalDateTime.ToString("MM-dd HH:mm") : ""));
                }
            }
            try
            {
                Clipboard.SetText(String.Join(Environment.NewLine, lines));
                _copyMessage = "已复制";
            }
            catch (System.Runtime.InteropServices.ExternalException) { _copyMessage = "重试"; }
            _copyUntil = now.AddSeconds(3);
            _copy.Content = _copyMessage;
        }

        private void AddRate(Panel panel, RateWindow window)
        {
            if (window == null) return;
            bool expired = window.ResetsAt.HasValue && window.ResetsAt.Value <= _at;
            var color = expired ? PanelTheme.Muted : PanelTheme.QuotaColor(window.Remaining);
            string right = expired ? "等待同步" : "剩 " + WidgetView.Percent(window.Remaining);
            if (!expired && window.ResetsAt.HasValue) right += "  ·  " + WidgetView.ResetText(window, _at);
            panel.Children.Add(PanelTheme.Row(PanelTheme.Text(WidgetView.WindowName(window), 11.5, _rim.Ink),
                PanelTheme.Text(right, 11, _rim.Accent(color), true)));
            panel.Children.Add(PanelTheme.Progress(expired ? (double?)null : window.Remaining, color));
        }

        private Border Metric(string title, string value, Brush color)
        {
            var content = new StackPanel();
            content.Children.Add(Muted(title));
            content.Children.Add(PanelTheme.Text(value, 17, color == PanelTheme.Ink ? _rim.Ink : _rim.Accent(color), true, WidgetFonts.Mono));
            return new Border {
                Padding = new Thickness(10, 7, 10, 7), CornerRadius = new CornerRadius(9),
                Background = PanelTheme.Tint, Child = content
            };
        }

        private void AddRefresh()
        {
            _refresh = PanelTheme.Button("刷新", _refetch);
            AddFooter(_refresh);
        }

        private void AddFooter(Button item)
        {
            item.Margin = new Thickness(0, 0, 7, 0);
            _footer.Children.Add(item);
        }

        private TextBlock Heading(string text, double size = 13) { return PanelTheme.Text(text, size, _rim.Ink, true); }
        private TextBlock Muted(string text) { return PanelTheme.Text(text, 11.5, _rim.Accent(PanelTheme.Muted)); }
        private static string Rate(double value) { return value.ToString("0.#", CultureInfo.InvariantCulture) + " tok/s"; }
        private static string ShortId(string id) { return id.Length <= 7 ? id : id.Substring(id.Length - 7); }

        private sealed class SessionOption
        {
            public readonly string Id;
            public readonly string Label;
            public SessionOption(string id, string label) { Id = id; Label = label; }
            public override string ToString() { return Label; }
        }
    }
}
