using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DragonQuota
{
    internal sealed class IridescentRim
    {
        private readonly RotateTransform _rotation;
        private readonly double _start;
        private readonly Dictionary<Color, Brush> _accents = new Dictionary<Color, Brush>();
        private bool _animating;

        public LinearGradientBrush Brush { get; private set; }
        public Brush Ink { get; private set; }

        public IridescentRim(bool strong = false)
        {
            _start = strong ? -22 : 14;
            _rotation = new RotateTransform(_start, .5, .5);
            Brush = NewGradient();
            if (strong)
            {
                Add("#624676", 0);
                Add("#7F61AD", .24);
                Add("#547D9A", .42);
                Add("#AE824C", .56);
                Add("#DAB26D", .66);
                Add("#8D6CAD", .78);
                Add("#624676", 1);
            }
            else
            {
                Add("#C28E4F", 0);
                Add("#F2D69C", .22);
                Add("#C39CCD", .41);
                Add("#8CAFBF", .55);
                Add("#FFEDC0", .69);
                Add("#E7BA78", .82);
                Add("#C28E4F", 1);
            }

            var ink = NewGradient();
            AddStop(ink, "#382A54", 0);
            AddStop(ink, "#5C3E76", .29);
            AddStop(ink, "#234F6A", .49);
            AddStop(ink, "#79511D", .63);
            AddStop(ink, "#4F386D", .81);
            AddStop(ink, "#382A54", 1);
            Ink = ink;
        }

        public Brush Accent(Brush color)
        {
            var solid = color as SolidColorBrush;
            if (solid == null) return color;
            Brush cached;
            if (_accents.TryGetValue(solid.Color, out cached)) return cached;

            var gradient = NewGradient();
            gradient.GradientStops.Add(new GradientStop(solid.Color, 0));
            gradient.GradientStops.Add(new GradientStop(Blend(solid.Color, Color.FromRgb(27, 55, 85)), .36));
            gradient.GradientStops.Add(new GradientStop(Blend(solid.Color, Color.FromRgb(131, 90, 38)), .62));
            gradient.GradientStops.Add(new GradientStop(solid.Color, 1));
            _accents[solid.Color] = gradient;
            return gradient;
        }

        public void SetVisible(bool visible)
        {
            bool animate = visible && SystemParameters.ClientAreaAnimation;
            if (_animating == animate) return;
            _animating = animate;
            if (!animate)
            {
                _rotation.BeginAnimation(RotateTransform.AngleProperty, null);
                return;
            }

            var sweep = new DoubleAnimation(_start, _start + 360, TimeSpan.FromSeconds(7))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            Timeline.SetDesiredFrameRate(sweep, 24);
            _rotation.BeginAnimation(RotateTransform.AngleProperty, sweep);
        }

        private void Add(string hex, double position)
        {
            AddStop(Brush, hex, position);
        }

        private LinearGradientBrush NewGradient()
        {
            return new LinearGradientBrush {
                StartPoint = new Point(0, .15), EndPoint = new Point(1, .85), RelativeTransform = _rotation
            };
        }

        private static void AddStop(LinearGradientBrush gradient, string hex, double position)
        {
            gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(hex), position));
        }

        private static Color Blend(Color first, Color second)
        {
            return Color.FromRgb((byte)((first.R * 2 + second.R) / 3),
                (byte)((first.G * 2 + second.G) / 3), (byte)((first.B * 2 + second.B) / 3));
        }
    }

    internal static class WidgetFonts
    {
        public static readonly FontFamily Sans = new FontFamily("Anthropic Sans Desktop, Anthropic Sans Web Display Light, Microsoft YaHei UI, Segoe UI");
        public static readonly FontFamily Serif = new FontFamily("Anthropic Serif Desktop, Anthropic Serif Web Display Light, Microsoft YaHei UI, Georgia");
        public static readonly FontFamily Mono = new FontFamily("Anthropic Mono Desktop, Anthropic Mono Web, Microsoft YaHei UI, Consolas");
    }
}
