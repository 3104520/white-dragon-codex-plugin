using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace DragonQuota
{
    internal static class PanelTheme
    {
        public static readonly FontFamily Font = WidgetFonts.Sans;
        public static readonly Brush Ink = WidgetView.Paint("#2F2442");
        public static readonly Brush Muted = WidgetView.Paint("#695D7B");
        public static readonly Brush Purple = WidgetView.Paint("#6947AC");
        public static readonly Brush Tint = WidgetView.Paint("#F1EAF9");
        public static readonly Brush Line = WidgetView.Paint("#E8DFF1");
        public static readonly Brush Paper = WidgetView.Paint("#FFFCFF");
        public static readonly Brush White = WidgetView.Paint("#FFFFFF");
        public static readonly Brush Green = WidgetView.Paint("#187E5A");
        public static readonly Brush Amber = WidgetView.Paint("#A55B23");
        public static readonly Brush Rose = WidgetView.Paint("#A43A61");

        private static readonly ControlTemplate ButtonChrome = (ControlTemplate)XamlReader.Parse(
            @"<ControlTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""Button"">
                <Border x:Name=""surface"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}""
                    BorderThickness=""{TemplateBinding BorderThickness}"" CornerRadius=""8"" Padding=""{TemplateBinding Padding}"">
                    <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center"" RecognizesAccessKey=""True""/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property=""IsMouseOver"" Value=""True""><Setter TargetName=""surface"" Property=""Opacity"" Value="".83""/></Trigger>
                    <Trigger Property=""IsPressed"" Value=""True""><Setter TargetName=""surface"" Property=""Opacity"" Value="".65""/></Trigger>
                    <Trigger Property=""IsEnabled"" Value=""False""><Setter TargetName=""surface"" Property=""Opacity"" Value="".48""/></Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>");

        private static readonly Style SliderStyle = (Style)XamlReader.Parse(
            @"<Style xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""Slider"">
                <Setter Property=""Template"">
                    <Setter.Value>
                        <ControlTemplate TargetType=""Slider"">
                            <Grid Background=""Transparent"">
                                <Border Background=""#EAE3F2"" CornerRadius=""3"" Height=""5"" VerticalAlignment=""Center""/>
                                <Track x:Name=""PART_Track"" VerticalAlignment=""Center"">
                                    <Track.DecreaseRepeatButton>
                                        <RepeatButton Command=""Slider.DecreaseLarge"">
                                            <RepeatButton.Template><ControlTemplate TargetType=""RepeatButton"">
                                                <Border Background=""#B08FE6"" Height=""5"" CornerRadius=""3""/>
                                            </ControlTemplate></RepeatButton.Template>
                                        </RepeatButton>
                                    </Track.DecreaseRepeatButton>
                                    <Track.IncreaseRepeatButton>
                                        <RepeatButton Command=""Slider.IncreaseLarge"">
                                            <RepeatButton.Template><ControlTemplate TargetType=""RepeatButton"">
                                                <Border Background=""Transparent"" Height=""21""/>
                                            </ControlTemplate></RepeatButton.Template>
                                        </RepeatButton>
                                    </Track.IncreaseRepeatButton>
                                    <Track.Thumb>
                                        <Thumb Cursor=""SizeWE"">
                                            <Thumb.Template><ControlTemplate TargetType=""Thumb"">
                                                <Ellipse Width=""15"" Height=""15"" Fill=""#7958C3"" Stroke=""#FFF9FF"" StrokeThickness=""1.5""/>
                                            </ControlTemplate></Thumb.Template>
                                        </Thumb>
                                    </Track.Thumb>
                                </Track>
                            </Grid>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>");

        public static TextBlock Text(string value, double size = 12, Brush color = null, bool bold = false, FontFamily font = null)
        {
            return new TextBlock {
                Text = value, FontFamily = font ?? Font, FontSize = size, Foreground = color ?? Ink,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static Button Button(string label, Action action, bool prominent = false)
        {
            var button = new Button {
                Template = ButtonChrome, Content = label, FontFamily = Font, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(10, 6, 10, 7), BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            Highlight(button, prominent);
            button.Click += (sender, args) => action();
            return button;
        }

        public static void Highlight(Button button, bool active)
        {
            button.Background = active ? Purple : Tint;
            button.BorderBrush = active ? Purple : Line;
            button.Foreground = active ? White : Ink;
        }

        public static Grid Row(FrameworkElement left, FrameworkElement right)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(right, 1);
            row.Children.Add(left);
            row.Children.Add(right);
            return row;
        }

        public static Slider Slider(double min, double max, int value)
        {
            return new Slider {
                Style = SliderStyle, Minimum = min, Maximum = max, Value = value, Height = 22,
                TickFrequency = 1, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true
            };
        }

        public static Border Rule()
        {
            return new Border { Background = Line, Height = 1, Margin = new Thickness(0, 12, 0, 12) };
        }

        public static Border Progress(double? percent, Brush color)
        {
            var bar = new Border {
                Height = 6, CornerRadius = new CornerRadius(3), Background = Tint, Margin = new Thickness(0, 6, 0, 4)
            };
            if (!percent.HasValue || percent.Value <= 0) return bar;
            double value = Math.Min(100, percent.Value);
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - value, GridUnitType.Star) });
            content.Children.Add(new Border { Background = color, CornerRadius = new CornerRadius(3) });
            bar.Child = content;
            return bar;
        }

        public static Brush QuotaColor(double percent)
        {
            return percent <= 15 ? Rose : percent <= 35 ? Amber : Green;
        }

        public static string Count(long? value) { return value.HasValue ? WidgetView.Compact(value.Value) : "—"; }

        public static string Percent(double? value) { return value.HasValue ? WidgetView.Percent(value.Value) : "—"; }
    }
}
