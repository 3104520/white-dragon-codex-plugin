using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace DragonQuota
{
    internal enum BubbleMode { Closed, Limits, Dialogue }

    internal sealed class WidgetView : Canvas
    {
        public const double BubbleWidth = 278;
        public const double BubbleHeight = 153;
        public const double CompactBubbleWidth = 176;
        public const double CompactBubbleHeight = 96;
        private static readonly Brush Plum = Paint("#50386E");
        private static readonly Brush Ink = Paint("#46335A");
        private static readonly Brush Muted = Paint("#4A3D60");
        private static readonly Brush LightInk = Paint("#564968");
        private static readonly Brush Porcelain = Paint("#FEFAFF");
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        private readonly TextBlock _heading;
        private readonly TextBlock _value;
        private readonly TextBlock _weekly;
        private readonly TextBlock _context;
        private readonly TextBlock _tokens;
        private readonly TextBlock _dialogue;
        private readonly Ellipse _thought;
        private readonly Ellipse _smallThought;
        private readonly Path _outline;
        private readonly Path _glaze;
        private readonly IridescentRim _rim = new IridescentRim(strong: true);
        private readonly ScaleTransform _breath = new ScaleTransform(1, 1);
        private readonly ScaleTransform _squash = new ScaleTransform(1, 1);
        private readonly TranslateTransform _bubbleLift = new TranslateTransform();
        private readonly DragonArt _art;
        private bool _dragging;
        private bool _breathing;
        private BubblePlacement _bubbleSide;
        private bool _compact;
        private bool _tight;
        private double _bubbleScale = 1;

        public readonly Canvas Bubble;
        public readonly Image Mascot;
        public BubbleMode Mode { get; private set; }

        public WidgetView(DragonArt art = null)
        {
            _art = art ?? new DragonArt(new WidgetSettings());
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            WidgetFonts.SmoothText(this);

            Bubble = new Canvas { Cursor = System.Windows.Input.Cursors.Hand, RenderTransform = _bubbleLift };
            _outline = new Path {
                Data = Geometry.Parse("M 132 5 C 200 3 260 27 272 64 C 287 107 242 139 171 146 C 98 154 35 138 12 108 C -13 76 9 39 50 22 C 74 12 101 6 132 5 Z"),
                Fill = new LinearGradientBrush(ColorOf("#FFFCFF"), ColorOf("#F7F0FE"), 90),
                Stroke = _rim.Brush, StrokeThickness = 3.8, StrokeLineJoin = PenLineJoin.Round,
                Effect = new DropShadowEffect { Color = ColorOf("#352154"), BlurRadius = 11, ShadowDepth = 3, Direction = 275, Opacity = .21 }
            };
            Bubble.Children.Add(_outline);
            Bubble.IsVisibleChanged += (sender, args) => _rim.SetVisible(Bubble.IsVisible);
            _glaze = new Path {
                Data = Geometry.Parse("M 17 67 C 24 49 45 32 70 23 M 77 20 L 82 18.5"),
                Stroke = Paint("#E7D8F5"), StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false
            };
            Bubble.Children.Add(_glaze);

            _heading = MakeLabel(Ink, FontWeights.SemiBold);
            _value = MakeLabel(Ink, FontWeights.Bold);
            _weekly = MakeLabel(Ink, FontWeights.SemiBold);
            _context = MakeLabel(Muted, FontWeights.Medium);
            _tokens = MakeLabel(LightInk, FontWeights.Medium);
            _dialogue = MakeLabel(Ink, FontWeights.SemiBold);
            _dialogue.FontFamily = WidgetFonts.Serif;
            _dialogue.TextWrapping = TextWrapping.Wrap;
            Children.Add(Bubble);

            _thought = CreateThought(23, 16, 3.2, -11);
            _smallThought = CreateThought(13, 10, 2.9, -13);
            Children.Add(_thought);
            Children.Add(_smallThought);

            var motion = new TransformGroup();
            motion.Children.Add(_breath);
            motion.Children.Add(_squash);
            Mascot = new Image {
                Source = _art.Get(DragonPose.Idle), Stretch = Stretch.Uniform,
                Cursor = System.Windows.Input.Cursors.Hand, RenderTransform = motion,
                RenderTransformOrigin = new Point(.5, 1)
            };
            RenderOptions.SetBitmapScalingMode(Mascot, BitmapScalingMode.HighQuality);
            Children.Add(Mascot);
            ArrangeWidget(new Size(329, 345), new Rect(178, 196, 149, 149),
                new Rect(15, 5, BubbleWidth, BubbleHeight), BubblePlacement.Above, false, 100);
            ShowBubble(BubbleMode.Closed, false);
            IsVisibleChanged += (sender, args) => SyncBreathing();
        }

        public void ArrangePreview(double scale)
        {
            const double inset = 8;
            scale = WidgetLayout.CompactScale(scale);
            var bubble = new Rect(inset, inset, CompactBubbleWidth * scale, CompactBubbleHeight * scale);
            Mascot.Visibility = Visibility.Collapsed;
            ArrangeWidget(new Size(bubble.Right + inset, bubble.Bottom + inset), new Rect(0, 0, 0, 0),
                bubble, BubblePlacement.Overlay, true, 100);
            ShowBubble(BubbleMode.Limits, false);
        }

        public void ArrangeWidget(Size viewport, Rect mascot, Rect bubble, BubblePlacement side, bool compact, int opacity)
        {
            Width = viewport.Width;
            Height = viewport.Height;
            _bubbleSide = side;
            _compact = compact;
            PlaceElement(Mascot, mascot);
            Mascot.Opacity = opacity / 100.0;
            PlaceElement(Bubble, bubble);
            SetZIndex(Bubble, side == BubblePlacement.Overlay ? 1 : 0);

            _bubbleScale = Math.Min(bubble.Width / (compact ? CompactBubbleWidth : BubbleWidth),
                bubble.Height / (compact ? CompactBubbleHeight : BubbleHeight));
            _tight = compact && _bubbleScale < .82;
            var shapeScale = new ScaleTransform(bubble.Width / BubbleWidth, bubble.Height / BubbleHeight);
            _outline.RenderTransform = _glaze.RenderTransform = shapeScale;
            if (compact)
            {
                LabelAt(_heading, 14, 9, 9, 158, 13);
                LabelAt(_value, 29, 8, _tight ? 12 : 32, 160, 24);
                LabelAt(_weekly, 13, 12, _tight ? 61 : 67, 152, 12.5);
                LabelAt(_dialogue, 14, 21, 17, 134, 13);
                _dialogue.MaxHeight = 64 * _bubbleScale;
            }
            else
            {
                LabelAt(_heading, 15, 12, 18, 254, 14);
                LabelAt(_value, 34, 12, 38, 254, 30);
                LabelAt(_weekly, 13, 12, 83, 254, 12);
                LabelAt(_context, 13, 12, 102, 254, 12);
                LabelAt(_tokens, 12.5, 12, 120, 254, 12);
                LabelAt(_dialogue, 16, 38, 51, 202);
                _dialogue.MaxHeight = 74 * _bubbleScale;
            }
            PositionThoughts(mascot, bubble, side);
            if (Mode != BubbleMode.Closed)
                UpdateBubbleVisibility(_dragging ? Visibility.Collapsed : Visibility.Visible);
        }

        private static void PlaceElement(FrameworkElement item, Rect bounds)
        {
            item.Width = bounds.Width;
            item.Height = bounds.Height;
            SetLeft(item, bounds.X);
            SetTop(item, bounds.Y);
        }

        private void LabelAt(TextBlock label, double fontSize, double x, double y, double width, double minimumFontSize = 0)
        {
            label.FontSize = Math.Max(minimumFontSize, fontSize * _bubbleScale);
            label.Width = width * _bubbleScale;
            SetLeft(label, x * _bubbleScale);
            SetTop(label, y * _bubbleScale);
        }

        private void PositionThoughts(Rect mascot, Rect bubble, BubblePlacement side)
        {
            if (side == BubblePlacement.Overlay) return;
            var center = new Point(mascot.X + mascot.Width / 2, mascot.Y + mascot.Height / 2);
            double scale = _bubbleScale;
            double inset = 19 * scale;
            Point from, to;
            if (side == BubblePlacement.Left || side == BubblePlacement.Right)
            {
                bool left = side == BubblePlacement.Left;
                from = new Point(left ? bubble.Right : bubble.Left,
                    Math.Max(bubble.Top + inset, Math.Min(bubble.Bottom - inset, center.Y - 11 * scale)));
                to = new Point(left ? mascot.Left : mascot.Right, center.Y);
            }
            else
            {
                bool below = side == BubblePlacement.Below;
                from = new Point(Math.Max(bubble.Left + inset, Math.Min(bubble.Right - inset, center.X - 11 * scale)),
                    below ? bubble.Top : bubble.Bottom);
                to = new Point(center.X, below ? mascot.Bottom : mascot.Top);
            }
            var distance = to - from;
            double trailScale = Math.Min(scale, distance.Length / 35);
            PositionThought(_thought, 21, 15, 3.1, from.X + distance.X * .3, from.Y + distance.Y * .31, trailScale);
            PositionThought(_smallThought, 12, 9, 2.8, from.X + distance.X * .73, from.Y + distance.Y * .76, trailScale);
        }

        private static void PositionThought(Ellipse item, double width, double height, double stroke, double x, double y, double scale)
        {
            item.Width = width * scale;
            item.Height = height * scale;
            item.StrokeThickness = stroke * scale;
            SetLeft(item, x - item.Width / 2);
            SetTop(item, y - item.Height / 2);
        }

        public void SetPose(DragonPose pose)
        {
            Mascot.Source = _art.Get(pose);
        }

        public void SetDragging(bool dragging)
        {
            _dragging = dragging;
            Mascot.Cursor = dragging ? System.Windows.Input.Cursors.SizeAll : System.Windows.Input.Cursors.Hand;
            if (dragging) _squash.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            ShowBubble(Mode, false);
            SyncBreathing();
        }

        public void ShowBubble(BubbleMode mode, bool animate = true)
        {
            var previous = Mode;
            Mode = mode;
            var visibility = mode == BubbleMode.Closed || _dragging ? Visibility.Collapsed : Visibility.Visible;
            Bubble.IsHitTestVisible = visibility == Visibility.Visible;
            if (animate && IsLoaded && !_dragging && previous != mode)
            {
                if (mode == BubbleMode.Closed && Bubble.Visibility == Visibility.Visible)
                {
                    var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(130));
                    fade.Completed += (sender, args) => { if (Mode == BubbleMode.Closed) ShowBubble(Mode, false); };
                    Bubble.BeginAnimation(OpacityProperty, fade);
                    _thought.Visibility = _smallThought.Visibility = Visibility.Collapsed;
                    return;
                }
                if (previous == BubbleMode.Closed)
                {
                    var duration = TimeSpan.FromMilliseconds(220);
                    var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                    Bubble.BeginAnimation(OpacityProperty, new DoubleAnimation(.22, 1, duration) { FillBehavior = FillBehavior.Stop });
                    var bubbleOffset = _bubbleSide == BubblePlacement.Left || _bubbleSide == BubblePlacement.Above ? 6 : -6;
                    var axis = _bubbleSide == BubblePlacement.Left || _bubbleSide == BubblePlacement.Right
                        ? TranslateTransform.XProperty : TranslateTransform.YProperty;
                    _bubbleLift.BeginAnimation(axis,
                        new DoubleAnimation(bubbleOffset, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
                }
            }
            if (!animate || visibility == Visibility.Collapsed)
            {
                Bubble.BeginAnimation(OpacityProperty, null);
                _bubbleLift.BeginAnimation(TranslateTransform.XProperty, null);
                _bubbleLift.BeginAnimation(TranslateTransform.YProperty, null);
            }
            Bubble.Visibility = visibility;
            UpdateBubbleVisibility(visibility);
        }

        private void UpdateBubbleVisibility(Visibility visibility)
        {
            _thought.Visibility = _smallThought.Visibility = _bubbleSide == BubblePlacement.Overlay ? Visibility.Collapsed : visibility;
            bool dialogue = Mode == BubbleMode.Dialogue;
            _dialogue.Visibility = dialogue ? Visibility.Visible : Visibility.Collapsed;
            var data = dialogue ? Visibility.Collapsed : Visibility.Visible;
            _value.Visibility = data;
            _weekly.Visibility = data;
            _context.Visibility = _tokens.Visibility = _compact ? Visibility.Collapsed : data;
            _heading.Visibility = _tight ? Visibility.Collapsed : data;
        }

        public void SetDialogue(string text)
        {
            _dialogue.Text = text;
            Bubble.ToolTip = text;
        }

        public void SetLimits(QuotaSample quota, LocalUsage local, bool loading, DateTimeOffset now,
            TurnSummary completion = null, string status = null)
        {
            RateWindow main = quota == null ? null : quota.Short ?? quota.Long;
            RateWindow weekly = quota == null || main == quota.Long ? null : quota.Long;
            string pool = quota == null ? "Codex" : quota.Name ?? "Codex";
            _heading.Text = completion != null ? "本轮已完成" : main == null ? "Codex 额度" :
                (_compact ? "" : pool + " · ") + WindowName(main) + "额度";

            bool resetPassed = main != null && main.ResetsAt.HasValue && main.ResetsAt.Value <= now;
            _value.Inlines.Clear();
            _value.Foreground = main == null || resetPassed ? _rim.Accent(Muted) : _rim.Ink;
            if (main == null || resetPassed)
            {
                _value.Inlines.Add(new Run(loading ? "读取中…" : resetPassed ? "等待同步" : "额度暂不可用") {
                    FontSize = Math.Max(14, (_compact ? 18 : 23) * _bubbleScale)
                });
            }
            else
            {
                if (!_compact)
                    _value.Inlines.Add(new Run("剩余 ") { FontSize = Math.Max(14, 17 * _bubbleScale), FontWeight = FontWeights.SemiBold });
                _value.Inlines.Add(new Run(Percent(main.Remaining)) { FontFamily = WidgetFonts.Mono, Foreground = _rim.QuotaInk });
            }

            Bubble.ToolTip = (String.IsNullOrEmpty(status) ? "" : status + "\n") + Details(quota, local, now);
            if (_compact)
            {
                _weekly.Text = main == null ? "" : !main.ResetsAt.HasValue ? "重置时间未知" :
                    resetPassed ? "重置后待同步" : "重置 " + LocalTime(main.ResetsAt.Value, now);
                if (main != null && status != null && status.Contains("缓存"))
                    _weekly.Text = _tight ? "额度缓存" : _weekly.Text + " · 缓存";
                return;
            }

            string reset = ResetText(main, now);
            string other = "";
            if (weekly != null)
            {
                bool current = !weekly.ResetsAt.HasValue || weekly.ResetsAt.Value > now;
                other = WindowName(weekly) + (current ? "剩 " + Percent(weekly.Remaining) : "待同步");
            }
            if (other.Length > 0 && reset.Length > 0) _weekly.Text = other + " · " + reset;
            else if (other.Length > 0 || reset.Length > 0) _weekly.Text = other + reset;
            else _weekly.Text = main != null || loading ? "" : "右键角色可重新查询";

            if (completion != null && completion.Tokens.HasValue)
            {
                _context.Text = "本轮新增 " + Compact(completion.Tokens.Value) + " tok";
            }
            else if (local != null && local.ContextPercent.HasValue)
            {
                _context.Text = "上下文 " + Percent(local.ContextPercent.Value) + " · " + Compact(local.ContextUsed.Value) + " / " + Compact(local.ContextLimit.Value);
            }
            else _context.Text = "";

            string source = status ?? (quota != null && !quota.FromService ? "会话记录 " + LocalTime(quota.At, now) : "");
            if (local != null && local.TotalTokens.HasValue)
            {
                string tokens = "已用 " + Compact(local.TotalTokens.Value) + " tok";
                _tokens.Text = (source.Length > 0 ? source : "最近 Codex 会话") + " · " + tokens;
            }
            else _tokens.Text = source;
        }

        public void Poke()
        {
            var bounceY = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            bounceY.KeyFrames.Add(Frame(.88, 65));
            bounceY.KeyFrames.Add(Frame(1.085, 170));
            bounceY.KeyFrames.Add(Frame(.98, 260));
            bounceY.KeyFrames.Add(Frame(1, 365));
            _squash.BeginAnimation(ScaleTransform.ScaleYProperty, bounceY);
        }

        private void SyncBreathing()
        {
            bool animate = IsVisible && Mascot.Visibility == Visibility.Visible && !_dragging;
            if (animate == _breathing) return;
            _breathing = animate;
            if (!animate)
            {
                _breath.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                return;
            }
            var idle = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            idle.KeyFrames.Add(Frame(1, 0));
            idle.KeyFrames.Add(Frame(.976, 700));
            idle.KeyFrames.Add(Frame(1.019, 1440));
            idle.KeyFrames.Add(Frame(1, 2180));
            _breath.BeginAnimation(ScaleTransform.ScaleYProperty, idle);
        }

        private static EasingDoubleKeyFrame Frame(double value, int millis)
        {
            return new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(millis)),
                new SineEase { EasingMode = EasingMode.EaseInOut });
        }

        private TextBlock MakeLabel(Brush color, FontWeight weight)
        {
            var label = new TextBlock {
                FontFamily = WidgetFonts.Sans, FontWeight = weight, SnapsToDevicePixels = true,
                Foreground = color == Ink ? _rim.Ink : _rim.Accent(color), TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false
            };
            Bubble.Children.Add(label);
            return label;
        }

        private static Ellipse CreateThought(double width, double height, double stroke, double angle)
        {
            return new Ellipse {
                Width = width, Height = height, Stroke = Plum, StrokeThickness = stroke, Fill = Porcelain,
                RenderTransform = new RotateTransform(angle), RenderTransformOrigin = new Point(.5, .5),
                IsHitTestVisible = false
            };
        }

        private static string Details(QuotaSample quota, LocalUsage local, DateTimeOffset now)
        {
            string text = quota == null ? "Codex 额度暂不可用" :
                (quota.FromService ? "Codex 官方接口 · 查询于 " : "Codex 会话记录 · 记录于 ") + LocalTime(quota.At, now);
            if (quota != null)
            {
                foreach (var window in new[] { quota.Short, quota.Long })
                {
                    if (window != null && window.ResetsAt.HasValue)
                        text += "\n" + WindowName(window) + "重置时间 " + window.ResetsAt.Value.ToLocalTime().ToString("MM-dd HH:mm");
                }
            }
            if (local != null && local.At != default(DateTimeOffset))
                text += "\nToken 来自最近记录的 Codex 会话 · " + LocalTime(local.At, now);
            return text;
        }

        public static string WindowName(RateWindow window)
        {
            if (window.Minutes == 10080) return "每周";
            if (window.Minutes > 0 && window.Minutes % 1440 == 0) return (window.Minutes / 1440) + " 天";
            if (window.Minutes > 0 && window.Minutes % 60 == 0) return (window.Minutes / 60) + " 小时";
            return window.Minutes > 0 ? window.Minutes + " 分钟" : "当前";
        }

        public static string ResetText(RateWindow window, DateTimeOffset now)
        {
            if (window == null || !window.ResetsAt.HasValue) return "";
            var remaining = window.ResetsAt.Value - now;
            if (remaining <= TimeSpan.Zero) return "等待重置数据";
            int minutes = (int)Math.Ceiling(remaining.TotalMinutes);
            if (minutes >= 1440) return (minutes / 1440) + " 天 " + ((minutes / 60) % 24) + " 小时后重置";
            if (minutes >= 60) return (minutes / 60) + " 小时 " + (minutes % 60) + " 分后重置";
            return minutes + " 分后重置";
        }

        public static string Percent(double value) { return value.ToString("0.#", Culture) + "%"; }

        public static string Compact(long value)
        {
            if (value >= 1000000000) return (value / 1000000000.0).ToString("0.#", Culture) + "B";
            if (value >= 1000000) return (value / 1000000.0).ToString("0.#", Culture) + "M";
            if (value >= 1000) return (value / 1000.0).ToString("0.#", Culture) + "K";
            return value.ToString(Culture);
        }

        public static string LocalTime(DateTimeOffset time, DateTimeOffset now)
        {
            var local = time.ToLocalTime();
            return local.ToString(local.Date == now.ToLocalTime().Date ? "HH:mm" : "MM-dd HH:mm");
        }

        public static Brush Paint(string text)
        {
            var brush = new SolidColorBrush(ColorOf(text));
            brush.Freeze();
            return brush;
        }

        private static Color ColorOf(string text) { return (Color)ColorConverter.ConvertFromString(text); }

    }
}
