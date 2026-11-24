using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DragonQuota
{
    internal sealed class WidgetWindow : Window
    {
        private static readonly string[] Notes = {
            "写累了就停一会儿。\n我在这儿帮你看着额度。",
            "这一角就留给我吧。\n龙翼折起来，刚好坐得下。",
            "还有想法没写下来吗？\n我陪你慢慢想。"
        };

        private readonly WidgetSettings _settings;
        private readonly DragonArt _art;
        private readonly WidgetView _view;
        private readonly StatsWindow _stats;
        private readonly SessionMonitor _monitor = new SessionMonitor();
        private readonly Random _random = new Random();
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _pointerTimer;
        private readonly DispatcherTimer _saveTimer;
        private readonly IntPtr _handle;
        private readonly int _processId;
        private TelemetryPanel _panel;

        private IntPtr _foreground;
        private IntPtr _host;
        private NativeWindows.Bounds _hostBounds;
        private Int32Rect _placement;
        private Int32Rect _panelPlacement;
        private WidgetLayout _layout;
        private double _layoutDpi;
        private int _layoutOpacity;
        private DockDrag _drag;
        private DragSurface _dragSurface;
        private bool _mouseWasDown;
        private bool _closed;

        private bool _localBusy;
        private bool _quotaBusy;
        private bool _quotaOffline;
        private DateTimeOffset _nextLocal;
        private DateTimeOffset _nextQuota;
        private DateTimeOffset _nextPaint;
        private DateTimeOffset _lastCompletion = DateTimeOffset.Now;
        private DateTimeOffset _bubbleUntil;
        private DateTimeOffset _celebrateUntil;
        private DateTimeOffset _pokeUntil;
        private DateTimeOffset _lastPoke;
        private int _pokes;
        private TurnSummary _completion;
        private LocalUsage _completionSession;
        private SessionSnapshot _sessions = new SessionSnapshot();
        private LocalUsage _session;
        private QuotaSample _live;

        public WidgetWindow(WidgetSettings settings)
        {
            _settings = settings;
            _art = new DragonArt(settings);
            _view = new WidgetView(_art);
            using (var process = Process.GetCurrentProcess()) _processId = process.Id;
            Title = "白龙额度挂件";
            Width = _view.Width;
            Height = _view.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Content = _view;
            _view.Mascot.MouseLeftButtonDown += StartPress;
            _view.Mascot.MouseLeftButtonUp += EndPress;
            _view.Mascot.MouseRightButtonUp += OpenDetails;
            _view.Bubble.MouseLeftButtonUp += (sender, args) => CycleBubble(false);
            _view.Bubble.MouseRightButtonUp += OpenDetails;

            _handle = new WindowInteropHelper(this).EnsureHandle();
            NativeWindows.KeepKeyboardOnHost(_handle);
            _stats = new StatsWindow();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _timer.Tick += Track;
            _timer.Start();
            _pointerTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
            _pointerTimer.Tick += TrackPointer;
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            _saveTimer.Tick += (sender, args) => SaveSettings();
            Closed += (sender, args) => {
                _closed = true;
                _timer.Stop();
                CancelPress();
                _pointerTimer.Stop();
                if (_saveTimer.IsEnabled) SaveSettings();
                if (_panel != null) _panel.Close();
                _stats.Close();
            };
        }

        private void Track(object sender, EventArgs args)
        {
            IntPtr foreground = NativeWindows.GetForegroundWindow();
            if (foreground != IntPtr.Zero && foreground != _foreground && !NativeWindows.BelongsTo(foreground, _processId))
            {
                _foreground = foreground;
                _host = NativeWindows.IsChatGpt(foreground) ? foreground : IntPtr.Zero;
            }
            if (!NativeWindows.TryGetBounds(_host, out _hostBounds) || !FitHost())
            {
                CancelPress();
                CloseBubble(false);
                if (_panel != null && _panel.IsVisible) _panel.Hide();
                if (_stats.IsVisible) _stats.Hide();
                if (IsVisible) Hide();
                return;
            }

            DateTimeOffset now = DateTimeOffset.Now;
            if (!_localBusy && now >= _nextLocal) RefreshLocal();
            if (LimitsVisible && !_quotaBusy && now >= _nextQuota) RefreshQuota();
            UpdateAnimations(now);
            if (LimitsVisible && now >= _nextPaint) PaintData(now);
            LayoutChildren();
        }

        private bool LimitsVisible { get { return _view.Mode == BubbleMode.Limits || (_panel != null && _panel.IsVisible); } }

        private bool FitHost()
        {
            if (_drag != null && _drag.Moving) return true;
            double dpi = NativeWindows.ScaleOf(_host);
            if (_hostBounds.Width < 130 * dpi || _hostBounds.Height < 130 * dpi) return false;
            var layout = WidgetLayout.Inside(_hostBounds, dpi, _settings);
            var placement = layout.Window;
            if (!IsVisible)
            {
                NativeWindows.Place(_handle, placement.X, placement.Y, placement.Width, placement.Height);
                Show();
                _placement = Int32Rect.Empty;
            }
            if (_placement != placement || !NativeWindows.IsWindowVisible(_handle)) Place(placement);
            var target = PresentationSource.FromVisual(this);
            double viewDpi = target == null || target.CompositionTarget == null ? dpi : target.CompositionTarget.TransformToDevice.M11;
            if (_layout.Mascot != layout.Mascot || _layout.Bubble != layout.Bubble || _layout.Window != placement ||
                _layout.BubbleSide != layout.BubbleSide || _layout.CompactBubble != layout.CompactBubble ||
                _layoutDpi != viewDpi || _layoutOpacity != _settings.Opacity)
            {
                _view.ArrangeWidget(new Size(placement.Width / viewDpi, placement.Height / viewDpi),
                    WidgetLayout.Relative(layout.Mascot, placement, viewDpi),
                    WidgetLayout.Relative(layout.Bubble, placement, viewDpi),
                    layout.BubbleSide, layout.CompactBubble, _settings.Opacity);
                _layoutDpi = viewDpi;
                _layoutOpacity = _settings.Opacity;
                _nextPaint = DateTimeOffset.MinValue;
            }
            _layout = layout;
            return true;
        }

        private void Place(Int32Rect position)
        {
            NativeWindows.Place(_handle, position.X, position.Y, position.Width, position.Height, true);
            _placement = position;
        }

        private void StartPress(object sender, MouseButtonEventArgs args)
        {
            if (_drag != null || _placement.IsEmpty) return;
            Point pointer;
            bool pressed;
            if (!NativeWindows.TryGetPointer(out pointer, out pressed)) pointer = _view.PointToScreen(args.GetPosition(_view));
            BeginPress(pointer);
            args.Handled = true;
        }

        private void BeginPress(Point pointer)
        {
            _drag = new DockDrag(pointer, _layout.Mascot, NativeWindows.ScaleOf(_host));
            CompositionTarget.Rendering += TrackPointer;
            _pointerTimer.Start();
        }

        private void EndPress(object sender, MouseButtonEventArgs args)
        {
            if (_drag == null) return;
            Point pointer;
            bool pressed;
            if (NativeWindows.TryGetPointer(out pointer, out pressed)) FinishPress(pointer);
            else CancelPress();
            args.Handled = true;
        }

        private void TrackPointer(object sender, EventArgs args)
        {
            bool watchingPopup = _view.Mode != BubbleMode.Closed || (_panel != null && _panel.IsVisible);
            if (_drag == null && !watchingPopup)
            {
                _pointerTimer.Stop();
                return;
            }
            Point pointer;
            bool pressed;
            if (!NativeWindows.TryGetPointer(out pointer, out pressed)) { CancelPress(); return; }
            if (_drag != null)
            {
                _mouseWasDown = pressed;
                if (!pressed) FinishPress(pointer);
                else MovePress(pointer);
                return;
            }
            bool down = pressed || NativeWindows.OtherMouseButtonDown();
            ObservePopupPress(down, down && !_mouseWasDown ? NativeWindows.WindowAt(pointer) : IntPtr.Zero);
        }

        private void ObservePopupPress(bool down, IntPtr clickedWindow)
        {
            if (down && !_mouseWasDown && !NativeWindows.BelongsTo(clickedWindow, _processId))
            {
                CloseBubble();
                if (_panel != null && _panel.IsVisible) _panel.Hide();
            }
            _mouseWasDown = down;
        }

        private void MovePress(Point pointer)
        {
            bool wereMoving = _drag.Moving;
            var position = _drag.Move(pointer, _hostBounds);
            if (!wereMoving && _drag.Moving)
            {
                CloseBubble(false);
                if (_panel != null && _panel.IsVisible) _panel.Hide();
                _view.SetDragging(true);
                _dragSurface = new DragSurface(position, _view.Mascot.Source, _view.Mascot.Opacity);
                Hide();
                _placement = position;
            }
            else if (_drag.Moving && _placement != position)
            {
                _dragSurface.Move(position);
                _placement = position;
            }
        }

        private void FinishPress(Point pointer)
        {
            if (_drag == null) return;
            MovePress(pointer);
            bool moved = _drag.Moving;
            var final = new Point(_placement.X, _placement.Y);
            var surface = _dragSurface;
            _dragSurface = null;
            CancelPress();

            try
            {
                if (!moved) CycleBubble(true);
                else
                {
                    double dpi = NativeWindows.ScaleOf(_host);
                    _settings.Position = new Point((final.X - _hostBounds.Left) / dpi, (final.Y - _hostBounds.Top) / dpi);
                    ChangeSettings();
                }
            }
            finally { if (surface != null) surface.Dispose(); }
        }

        private void CancelPress()
        {
            if (_drag == null) return;
            bool moved = _drag.Moving;
            _drag = null;
            CompositionTarget.Rendering -= TrackPointer;
            if (moved) _view.SetDragging(false);
            if (_dragSurface != null)
            {
                _dragSurface.Dispose();
                _dragSurface = null;
            }
        }

        private void CycleBubble(bool poke)
        {
            _completion = null;
            _completionSession = null;
            if (poke)
            {
                DateTimeOffset now = DateTimeOffset.Now;
                _pokes = now - _lastPoke < TimeSpan.FromMilliseconds(620) ? _pokes + 1 : 1;
                _lastPoke = now;
                _view.Poke();
                _pokeUntil = now.AddMilliseconds(400);
                if (_pokes == 3 && _settings.Celebrate)
                {
                    _celebrateUntil = now.AddSeconds(2);
                    _pokes = 0;
                }
                UpdatePose(now);
            }
            switch (_view.Mode)
            {
                case BubbleMode.Closed:
                    ShowLimits();
                    break;
                case BubbleMode.Limits:
                    IList<string> lines = _settings.Lines.Count > 0 ? (IList<string>)_settings.Lines : Notes;
                    _view.SetDialogue(ExpandNote(lines[_random.Next(lines.Count)], CurrentQuota(), _session, DateTimeOffset.Now));
                    OpenBubble(BubbleMode.Dialogue, DateTimeOffset.Now);
                    break;
                default:
                    CloseBubble();
                    break;
            }
        }

        internal static string ExpandNote(string line, QuotaSample quota, LocalUsage session, DateTimeOffset now)
        {
            RateWindow[] windows = quota == null ? new RateWindow[0] : new[] { quota.Short, quota.Long };
            var hours = windows.FirstOrDefault(item => item != null && item.Minutes == 300);
            var week = windows.FirstOrDefault(item => item != null && item.Minutes == 10080);
            return line.Replace("{quota_5h}", Remaining(hours, now)).Replace("{quota_week}", Remaining(week, now))
                .Replace("{context_pct}", PanelTheme.Percent(session == null ? null : session.ContextPercent))
                .Replace("{tokens}", PanelTheme.Count(session == null ? null : session.TotalTokens))
                .Replace("{model}", session == null ? "Codex" : session.Model ?? "Codex");
        }

        private static string Remaining(RateWindow window, DateTimeOffset now)
        {
            return window == null || (window.ResetsAt.HasValue && window.ResetsAt.Value <= now)
                ? "—" : WidgetView.Percent(window.Remaining);
        }

        private void ShowLimits()
        {
            DateTimeOffset now = DateTimeOffset.Now;
            OpenBubble(BubbleMode.Limits, now);
            PaintData(now);
        }

        private void OpenBubble(BubbleMode mode, DateTimeOffset now)
        {
            _bubbleUntil = now.AddSeconds(10);
            _view.ShowBubble(mode);
            WatchPopup();
        }

        private void CloseBubble(bool animate = true)
        {
            _completion = null;
            _completionSession = null;
            _view.ShowBubble(BubbleMode.Closed, animate);
        }

        private void WatchPopup()
        {
            if (_pointerTimer.IsEnabled) return;
            Point point;
            bool pressed;
            NativeWindows.TryGetPointer(out point, out pressed);
            _mouseWasDown = pressed || NativeWindows.OtherMouseButtonDown();
            _pointerTimer.Start();
        }

        private void OpenDetails(object sender, MouseButtonEventArgs args)
        {
            args.Handled = true;
            OpenPanel(PanelPage.Summary);
        }

        private void OpenPanel(PanelPage page)
        {
            if (_panel == null)
            {
                _panel = new TelemetryPanel(_settings, _art, ChangeSettings, RefreshNow, LayoutChildren);
                _panel.Deactivated += (sender, args) => Dispatcher.BeginInvoke(new Action(() => {
                    if (_panel.IsVisible && !NativeWindows.BelongsTo(NativeWindows.GetForegroundWindow(), _processId)) _panel.Hide();
                }), DispatcherPriority.Background);
            }
            _panel.SetPage(page);
            _panelPlacement = Int32Rect.Empty;
            PlacePanel(true);
            WatchPopup();
            PaintData(DateTimeOffset.Now);
        }

        private void ChangeSettings()
        {
            _session = _sessions.Select(_settings.PinnedSession);
            _stats.UpdateUsage(_session);
            if (!_settings.CompletionBubble && _completion != null)
            {
                CloseBubble();
            }
            if (!_settings.Celebrate) _celebrateUntil = DateTimeOffset.MinValue;
            UpdatePose(DateTimeOffset.Now);
            if (_panel != null) _panel.SyncCorner();
            if (_host != IntPtr.Zero) FitHost();
            _nextPaint = DateTimeOffset.MinValue;
            LayoutChildren();
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void SaveSettings()
        {
            _saveTimer.Stop();
            _settings.Save();
        }

        private void LayoutChildren()
        {
            if (_host == IntPtr.Zero) return;
            if (_settings.ShowStats) _stats.Follow(_hostBounds, NativeWindows.ScaleOf(_host));
            else if (_stats.IsVisible) _stats.Hide();
            if (_panel != null && _panel.IsVisible) PlacePanel(false);
        }

        private void PlacePanel(bool open)
        {
            if (_host == IntPtr.Zero || _panel == null) return;
            double dpi = NativeWindows.ScaleOf(_host);
            int margin = (int)Math.Round(12 * dpi);
            int width = Math.Min(_hostBounds.Width - 2 * margin, (int)Math.Round(TelemetryPanel.PanelWidth * dpi));
            int height = Math.Min(_hostBounds.Height - 2 * margin, (int)Math.Round(_panel.PanelHeight * dpi));
            if (width < 280 * dpi || height < 210 * dpi)
            {
                if (_panel.IsVisible) _panel.Hide();
                return;
            }
            int before = _layout.Mascot.X - width - margin;
            int after = _layout.Mascot.X + _layout.Mascot.Width + margin;
            bool right = after + width <= _hostBounds.Right - margin || before < _hostBounds.Left + margin;
            int x = WidgetLayout.Clamp(right ? after : before, _hostBounds.Left + margin, _hostBounds.Right - margin - width);
            int y = WidgetLayout.Clamp(_layout.Mascot.Y + _layout.Mascot.Height - height,
                _hostBounds.Top + margin, _hostBounds.Bottom - margin - height);
            var placement = new Int32Rect(x, y, width, height);
            IntPtr handle = new WindowInteropHelper(_panel).EnsureHandle();
            if (open && !_panel.IsVisible)
            {
                NativeWindows.Place(handle, x, y, width, height);
                _panel.Show();
            }
            if (_panelPlacement != placement || !NativeWindows.IsWindowVisible(handle))
            {
                NativeWindows.Place(handle, x, y, width, height, true);
                _panelPlacement = placement;
            }
            if (open) _panel.Activate();
        }

        private void RefreshNow()
        {
            _nextQuota = _nextLocal = DateTimeOffset.MinValue;
            if (!_localBusy) RefreshLocal();
            if (!_quotaBusy) RefreshQuota();
            if (_panel == null || !_panel.IsVisible) ShowLimits();
            else PaintData(DateTimeOffset.Now);
        }

        private async void RefreshLocal()
        {
            _localBusy = true;
            _nextLocal = DateTimeOffset.Now.AddSeconds(1.8);
            try
            {
                string pin = _settings.PinnedSession;
                var fresh = await Task.Run(() => _monitor.Read(pin));
                if (_closed) return;
                ApplySessions(fresh, DateTimeOffset.Now);
            }
            finally { _localBusy = false; }
        }

        internal void ApplySessions(SessionSnapshot fresh, DateTimeOffset now)
        {
            var before = _session;
            _sessions = fresh;
            _session = fresh.Select(_settings.PinnedSession);
            if (_session != before)
            {
                _stats.UpdateUsage(_session);
                if (IsVisible) LayoutChildren();
            }
            var finishedSession = _settings.PinnedSession != null ? _session : fresh.Recent
                .Where(item => item.LatestCompletion != null).OrderByDescending(item => item.LatestCompletion.At).FirstOrDefault();
            var completed = finishedSession == null ? null : finishedSession.LatestCompletion;
            if (completed != null && completed.At > _lastCompletion && completed.At <= now.AddSeconds(5))
            {
                _lastCompletion = completed.At;
                if (IsVisible && now - completed.At < TimeSpan.FromSeconds(25))
                {
                    if (_settings.Celebrate) _celebrateUntil = now.AddSeconds(2.6);
                    if (_settings.CompletionBubble || _settings.Celebrate) _view.Poke();
                    if (_settings.CompletionBubble)
                    {
                        _completion = completed;
                        _completionSession = finishedSession;
                        OpenBubble(BubbleMode.Limits, now);
                        if (!_quotaBusy && _nextQuota <= now.AddMinutes(1.7)) RefreshQuota();
                    }
                }
            }
            UpdateAnimations(now);
            if (LimitsVisible && (_session != before || now >= _nextPaint)) PaintData(now);
        }

        internal void UpdateAnimations(DateTimeOffset now)
        {
            if (_view.Mode != BubbleMode.Closed && now >= _bubbleUntil) CloseBubble();
            UpdatePose(now);
        }

        private void UpdatePose(DateTimeOffset now)
        {
            if (now < _celebrateUntil) _view.SetPose(DragonPose.Celebrate);
            else if (now < _pokeUntil) _view.SetPose(DragonPose.Poke);
            else _view.SetPose(_session != null && _session.IsWorking(now) ? DragonPose.Thinking : DragonPose.Idle);
        }

        private async void RefreshQuota()
        {
            _quotaBusy = true;
            _nextQuota = DateTimeOffset.Now.AddMinutes(2);
            try
            {
                var sample = await Task.Run(() => CodexUsage.ReadLiveAsync(_settings.Proxy));
                _quotaOffline = sample == null;
                if (sample != null) _live = sample;
            }
            finally { _quotaBusy = false; }
            if (!_closed) PaintData(DateTimeOffset.Now);
        }

        private QuotaSample CurrentQuota()
        {
            if (_live != null && !_quotaOffline) return _live;
            if (_live != null && (_sessions.Quota == null || _live.At > _sessions.Quota.At)) return _live.AsCache();
            return _sessions.Quota;
        }

        private void PaintData(DateTimeOffset now)
        {
            _nextPaint = now.AddSeconds(4);
            var quota = CurrentQuota();
            if (_view.Mode == BubbleMode.Limits) _view.SetLimits(quota, _completionSession ?? _session, _quotaBusy, now, _completion);
            if (_panel != null && _panel.IsVisible) _panel.UpdateData(_sessions, _session, quota, _quotaBusy, now);
        }

    }
}
