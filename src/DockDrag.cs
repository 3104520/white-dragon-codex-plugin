using System;
using System.Windows;

namespace DragonQuota
{
    internal sealed class DockDrag
    {
        private readonly Point _start;
        private readonly Int32Rect _origin;
        private readonly double _threshold;
        public bool Moving { get; private set; }

        public DockDrag(Point start, Int32Rect origin, double dpi)
        {
            _start = start;
            _origin = origin;
            _threshold = 4 * dpi;
        }

        public Int32Rect Move(Point pointer, NativeWindows.Bounds host)
        {
            var distance = pointer - _start;
            if (!Moving && Math.Max(Math.Abs(distance.X), Math.Abs(distance.Y)) < _threshold) return _origin;
            Moving = true;
            double x = Math.Max(host.Left, Math.Min(host.Right - _origin.Width, _origin.X + distance.X));
            double y = Math.Max(host.Top, Math.Min(host.Bottom - _origin.Height, _origin.Y + distance.Y));
            return new Int32Rect((int)Math.Round(x), (int)Math.Round(y), _origin.Width, _origin.Height);
        }
    }
}
