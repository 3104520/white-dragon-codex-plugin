using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace DragonQuota
{
    internal sealed class StatsWindow : Window
    {
        private readonly IntPtr _handle;
        private readonly TextBlock _text;
        private Int32Rect _position;

        public StatsWindow()
        {
            Title = "白龙会话统计栏";
            Width = 382;
            Height = 24;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowActivated = false;
            ShowInTaskbar = false;
            Topmost = true;
            _text = PanelTheme.Text("", 11, PanelTheme.Ink);
            _text.TextAlignment = TextAlignment.Center;
            Content = new Border {
                CornerRadius = new CornerRadius(11), Background = WidgetView.Paint("#F7F1FC"),
                BorderBrush = new IridescentRim().Brush, BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 2, 8, 2), Child = _text
            };
            _handle = new WindowInteropHelper(this).EnsureHandle();
            NativeWindows.KeepKeyboardOnHost(_handle, true);
        }

        public void UpdateUsage(LocalUsage session)
        {
            if (session == null)
            {
                _text.Text = "";
                return;
            }
            _text.Text = "上下文 " + PanelTheme.Percent(session.ContextPercent) +
                "   ·   " + PanelTheme.Count(session.TotalTokens) + " tok" +
                "   ·   缓存 " + PanelTheme.Percent(session.CachePercent) + "   ·   " + session.Turns + " 轮";
        }

        public void Follow(NativeWindows.Bounds host, double dpi)
        {
            if (_text.Text.Length == 0 || host.Width < 500 * dpi || host.Height < 280 * dpi)
            {
                if (IsVisible) Hide();
                return;
            }
            int width = (int)Math.Round(382 * dpi);
            int height = (int)Math.Round(24 * dpi);
            int x = host.Left + (host.Width - width) / 2;
            int y = host.Bottom - height - (int)Math.Round(5 * dpi);
            var rect = new Int32Rect(x, y, width, height);
            if (!IsVisible)
            {
                NativeWindows.Place(_handle, x, y, width, height);
                Show();
                _position = Int32Rect.Empty;
            }
            if (_position != rect || !NativeWindows.IsWindowVisible(_handle))
            {
                NativeWindows.Place(_handle, x, y, width, height, true);
                _position = rect;
            }
        }
    }
}
