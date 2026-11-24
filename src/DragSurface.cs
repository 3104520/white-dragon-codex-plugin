using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace DragonQuota
{
    internal sealed class DragSurface : IDisposable
    {
        private readonly HwndSource _source;
        public IntPtr Handle { get { return _source.Handle; } }

        public DragSurface(Int32Rect bounds, ImageSource sprite, double opacity)
        {
            const int popup = unchecked((int)0x80000000);
            const int noActivate = 0x08000000;
            const int toolWindow = 0x00000080;
            const int clickThrough = 0x00000020;
            var options = new HwndSourceParameters("白龙拖动预览", bounds.Width, bounds.Height) {
                PositionX = bounds.X, PositionY = bounds.Y, WindowStyle = popup,
                ExtendedWindowStyle = noActivate | toolWindow | clickThrough,
                UsesPerPixelOpacity = true
            };
            _source = new HwndSource(options);
            double dpi = _source.CompositionTarget.TransformToDevice.M11;
            var picture = new Image {
                Source = sprite, Opacity = opacity, Stretch = Stretch.Uniform,
                Width = bounds.Width / dpi, Height = bounds.Height / dpi, SnapsToDevicePixels = true
            };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            _source.RootVisual = picture;
            NativeWindows.Place(Handle, bounds.X, bounds.Y, bounds.Width, bounds.Height, true);
        }

        public void Move(Int32Rect position)
        {
            NativeWindows.Move(Handle, position.X, position.Y);
        }

        public void Dispose()
        {
            _source.Dispose();
        }
    }
}
