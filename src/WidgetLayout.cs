using System;
using System.Windows;

namespace DragonQuota
{
    internal struct WidgetLayout
    {
        public Int32Rect Window;
        public Int32Rect Mascot;
        public Int32Rect Bubble;
        public bool BubbleBelow;

        public static WidgetLayout Inside(NativeWindows.Bounds host, double dpi, WidgetSettings settings)
        {
            int margin = (int)Math.Round(7 * dpi);
            int size = Math.Max(1, Math.Min((int)Math.Round(settings.MascotSize * dpi),
                Math.Min(host.Width, host.Height) - margin * 2));
            bool left = settings.Corner == DockCorner.BottomLeft || settings.Corner == DockCorner.TopLeft;
            bool top = settings.Corner == DockCorner.TopLeft || settings.Corner == DockCorner.TopRight;
            int avatarX = left ? host.Left + margin : host.Right - margin - size;
            int avatarY = top ? host.Top + margin : host.Bottom - margin - size;
            if (settings.Position.HasValue)
            {
                avatarX = host.Left + (int)Math.Round(settings.Position.Value.X * dpi);
                avatarY = host.Top + (int)Math.Round(settings.Position.Value.Y * dpi);
            }
            avatarX = Clamp(avatarX, host.Left, host.Right - size);
            avatarY = Clamp(avatarY, host.Top, host.Bottom - size);

            int above = avatarY - host.Top - margin;
            int below = host.Bottom - avatarY - size - margin;
            double scale = dpi * settings.BubbleSize / 100.0;
            int preferredGap = (int)Math.Round(35 * dpi);
            bool bubbleBelow = above < WidgetView.BubbleHeight * scale + preferredGap && below > above;
            int available = Math.Max(1, bubbleBelow ? below : above);
            int gap = Math.Min(preferredGap, Math.Max(2, available / 5));
            scale = Math.Max(.18, Math.Min(scale, Math.Min((host.Width - margin * 2) / WidgetView.BubbleWidth,
                (available - gap) / WidgetView.BubbleHeight)));
            int width = (int)Math.Round(WidgetView.BubbleWidth * scale);
            int height = (int)Math.Round(WidgetView.BubbleHeight * scale);
            int bubbleX = Clamp(avatarX + (size - width) / 2, host.Left + margin, host.Right - margin - width);
            int bubbleY = Clamp(bubbleBelow ? avatarY + size + gap : avatarY - gap - height, host.Top, host.Bottom - height);

            const double shadowPadding = 10;
            int pad = (int)Math.Ceiling(shadowPadding * dpi);
            int x = Math.Min(avatarX, bubbleX - pad);
            int y = Math.Min(avatarY - (int)Math.Ceiling(size * .12), bubbleY - pad);
            int right = Math.Max(avatarX + size, bubbleX + width + pad);
            int bottom = Math.Max(avatarY + size, bubbleY + height + pad);
            return new WidgetLayout {
                Window = new Int32Rect(x, y, right - x, bottom - y),
                Mascot = new Int32Rect(avatarX, avatarY, size, size),
                Bubble = new Int32Rect(bubbleX, bubbleY, width, height),
                BubbleBelow = bubbleBelow
            };
        }

        public static Rect Relative(Int32Rect item, Int32Rect window, double dpi)
        {
            return new Rect((item.X - window.X) / dpi, (item.Y - window.Y) / dpi,
                item.Width / dpi, item.Height / dpi);
        }

        public static int Clamp(int value, int low, int high) { return Math.Max(low, Math.Min(high, value)); }
    }
}
