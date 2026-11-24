using System;
using System.Windows;

namespace DragonQuota
{
    internal enum BubblePlacement { Above, Below, Left, Right, Overlay }

    internal struct WidgetLayout
    {
        public Int32Rect Window;
        public Int32Rect Mascot;
        public Int32Rect Bubble;
        public BubblePlacement BubbleSide;
        public bool CompactBubble;

        public static WidgetLayout Inside(NativeWindows.Bounds host, double dpi, WidgetSettings settings)
        {
            int margin = Math.Min((int)Math.Round(7 * dpi), Math.Max(0, (Math.Min(host.Width, host.Height) - 1) / 2));
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

            var mascot = new Int32Rect(avatarX, avatarY, size, size);
            var inner = new NativeWindows.Bounds {
                Left = host.Left + margin, Top = host.Top + margin,
                Right = host.Right - margin, Bottom = host.Bottom - margin
            };
            double requested = dpi * settings.BubbleSize / 100.0;
            int gap = (int)Math.Round(35 * dpi);
            int sideGap = (int)Math.Round(20 * dpi);
            double baseWidth = WidgetView.BubbleWidth, baseHeight = WidgetView.BubbleHeight;
            double fit;
            var direction = BestSide(inner, mascot, baseWidth, baseHeight, gap, sideGap, requested, out fit);
            bool compact = fit < Math.Min(requested, .9 * dpi);
            if (compact)
            {
                baseWidth = WidgetView.CompactBubbleWidth;
                baseHeight = WidgetView.CompactBubbleHeight;
                gap = sideGap;
                direction = BestSide(inner, mascot, baseWidth, baseHeight, gap, sideGap, requested, out fit);
                if (fit < Math.Min(requested, .85 * dpi))
                {
                    direction = BubblePlacement.Overlay;
                    fit = Math.Min(inner.Width / baseWidth, inner.Height / baseHeight);
                }
            }

            double scale = Math.Min(requested, fit);
            int width = Math.Max(1, (int)Math.Round(baseWidth * scale));
            int height = Math.Max(1, (int)Math.Round(baseHeight * scale));
            int bubbleX = avatarX + (size - width) / 2;
            int bubbleY = avatarY + (size - height) / 2;
            switch (direction)
            {
                case BubblePlacement.Above: bubbleY = avatarY - gap - height; break;
                case BubblePlacement.Below: bubbleY = avatarY + size + gap; break;
                case BubblePlacement.Left: bubbleX = avatarX - sideGap - width; break;
                case BubblePlacement.Right: bubbleX = avatarX + size + sideGap; break;
                case BubblePlacement.Overlay:
                    // Prefer the top in a tight window so the face and a drag target remain visible.
                    int spaceAbove = avatarY - inner.Top;
                    int spaceBelow = inner.Bottom - avatarY - size;
                    bubbleY = spaceBelow > spaceAbove + height / 3 ? inner.Bottom - height : inner.Top;
                    break;
            }
            bubbleX = Clamp(bubbleX, inner.Left, inner.Right - width);
            bubbleY = Clamp(bubbleY, inner.Top, inner.Bottom - height);

            const double shadowPadding = 10;
            int pad = (int)Math.Ceiling(shadowPadding * dpi);
            int x = Math.Min(avatarX, bubbleX - pad);
            int y = Math.Min(avatarY - (int)Math.Ceiling(size * .12), bubbleY - pad);
            int right = Math.Max(avatarX + size, bubbleX + width + pad);
            int bottom = Math.Max(avatarY + size, bubbleY + height + pad);
            return new WidgetLayout {
                Window = new Int32Rect(x, y, right - x, bottom - y),
                Mascot = mascot,
                Bubble = new Int32Rect(bubbleX, bubbleY, width, height),
                BubbleSide = direction,
                CompactBubble = compact
            };
        }

        private static BubblePlacement BestSide(NativeWindows.Bounds inner, Int32Rect mascot, double width, double height,
            int gap, int sideGap, double requested, out double fit)
        {
            double above = Math.Min(inner.Width / width, (mascot.Y - gap - inner.Top) / height);
            double below = Math.Min(inner.Width / width, (inner.Bottom - mascot.Y - mascot.Height - gap) / height);
            double left = Math.Min(inner.Height / height, (mascot.X - sideGap - inner.Left) / width);
            double right = Math.Min(inner.Height / height, (inner.Right - mascot.X - mascot.Width - sideGap) / width);
            double bestSide = Math.Max(left, right);
            if (above >= requested || (above >= below && above >= bestSide))
            {
                fit = above;
                return BubblePlacement.Above;
            }
            if (below >= requested || below >= bestSide)
            {
                fit = below;
                return BubblePlacement.Below;
            }
            fit = bestSide;
            return left > right ? BubblePlacement.Left : BubblePlacement.Right;
        }

        public static Rect Relative(Int32Rect item, Int32Rect window, double dpi)
        {
            return new Rect((item.X - window.X) / dpi, (item.Y - window.Y) / dpi,
                item.Width / dpi, item.Height / dpi);
        }

        public static int Clamp(int value, int low, int high) { return Math.Max(low, Math.Min(high, value)); }
    }
}
