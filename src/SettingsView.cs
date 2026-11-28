using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;

namespace DragonQuota
{
    internal sealed class SettingsView : StackPanel
    {
        private readonly WidgetSettings _settings;
        private readonly DragonArt _art;
        private readonly WidgetView _bubblePreview;
        private readonly Action _apply;
        private readonly Dictionary<DockCorner, Button> _corners = new Dictionary<DockCorner, Button>();
        private readonly Dictionary<DragonPose, Image> _pictures = new Dictionary<DragonPose, Image>();
        private readonly Dictionary<DragonPose, TextBlock> _artStates = new Dictionary<DragonPose, TextBlock>();
        private readonly TextBlock _artFeedback = PanelTheme.Text("", 11, PanelTheme.Muted);

        public SettingsView(WidgetSettings settings, DragonArt art, Action apply)
        {
            _settings = settings;
            _art = art;
            _apply = apply;
            _bubblePreview = new WidgetView(art) { HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
            AutomationProperties.SetName(_bubblePreview, "额度气泡预览");
            AddSlider("挂件大小", 64, 180, settings.MascotSize, "px", value => settings.MascotSize = value);
            AddBubbleSize();
            AddSlider("龙娘透明度", 35, 100, settings.Opacity, "%", value => settings.Opacity = value);
            Children.Add(PanelTheme.Rule());
            AddQuotaRefresh();
            Children.Add(PanelTheme.Rule());
            AddArt();
            Children.Add(PanelTheme.Rule());
            AddDialogue();
            Children.Add(PanelTheme.Rule());
            AddToggle("输入框下方统计栏", () => settings.ShowStats, value => settings.ShowStats = value, "显示中", "已隐藏");
            AddToggle("对话完成额度气泡 · 10 秒", () => settings.CompletionBubble, value => settings.CompletionBubble = value, "已开启", "已关闭");
            AddToggle("思考完呲牙笑彩蛋", () => settings.Celebrate, value => settings.Celebrate = value, "已开启", "已关闭");
            Children.Add(PanelTheme.Rule());
            AddCorners();
            UpdateArt();
            UpdateCorner();
        }

        public void UpdateCorner()
        {
            foreach (var corner in _corners)
                PanelTheme.Highlight(corner.Value, !_settings.Position.HasValue && corner.Key == _settings.Corner);
        }

        public void UpdateBubblePreview(QuotaSample quota, LocalUsage session, bool loading, DateTimeOffset now, string status)
        {
            _bubblePreview.SetLimits(quota, session, loading, now, status: status);
        }

        private void AddQuotaRefresh()
        {
            Children.Add(PanelTheme.Text("前台额度查询间隔", 13, bold: true));
            var row = new UniformGrid { Columns = 3, Margin = new Thickness(0, 6, 0, 10) };
            var buttons = new Dictionary<int, Button>();
            foreach (int seconds in new[] { 10, 15, 60 })
            {
                var button = PanelTheme.Button(seconds + " 秒", () => {
                    _settings.QuotaRefreshSeconds = seconds;
                    foreach (var item in buttons) PanelTheme.Highlight(item.Value, item.Key == seconds);
                    _apply();
                });
                buttons.Add(seconds, button);
                PanelTheme.Highlight(button, seconds == _settings.QuotaRefreshSeconds);
                AddCell(row, button);
            }
            Children.Add(row);
        }

        private void AddSlider(string title, int min, int max, int value, string suffix, Action<int> apply)
        {
            var indicator = PanelTheme.Text(value + suffix, 12, PanelTheme.Purple, true);
            Children.Add(PanelTheme.Row(PanelTheme.Text(title, 13, bold: true), indicator));
            var slider = PanelTheme.Slider(min, max, value);
            AutomationProperties.SetName(slider, title);
            slider.Margin = new Thickness(0, 4, 0, 13);
            slider.ValueChanged += (sender, args) => {
                int amount = (int)Math.Round(args.NewValue);
                indicator.Text = amount + suffix;
                apply(amount);
                _apply();
            };
            Children.Add(slider);
        }

        private void AddBubbleSize()
        {
            var indicator = PanelTheme.Text("", 12, PanelTheme.Purple, true);
            Children.Add(PanelTheme.Row(PanelTheme.Text("气泡大小", 13, bold: true), indicator));
            var row = new UniformGrid { Columns = 6, Margin = new Thickness(0, 7, 0, 2) };
            var preview = new Border { Child = _bubblePreview, ClipToBounds = true, Margin = new Thickness(0, 0, 0, 10) };
            Action fitPreview = () => {
                double width = preview.ActualWidth > 0 ? preview.ActualWidth : TelemetryPanel.PanelWidth - 50;
                double maximum = Math.Max(.65, Math.Min(1.6, (width - 16) / WidgetView.CompactBubbleWidth));
                double fraction = (_settings.BubbleSize - 65) / 95.0;
                _bubblePreview.ArrangePreview(.65 + fraction * (maximum - .65));
            };
            preview.SizeChanged += (sender, args) => { if (args.WidthChanged) fitPreview(); };
            var presets = new Dictionary<int, Button>();
            Action update = () => {
                double scale = _settings.BubbleSize / 100.0;
                indicator.Text = _settings.BubbleSize + "% · " + Math.Round(WidgetView.BubbleWidth * scale) +
                    "×" + Math.Round(WidgetView.BubbleHeight * scale);
                foreach (var item in presets) PanelTheme.Highlight(item.Value, item.Key == _settings.BubbleSize);
                row.Children[0].IsEnabled = _settings.BubbleSize > 65;
                row.Children[5].IsEnabled = _settings.BubbleSize < 160;
                fitPreview();
            };
            Action<int> set = value => {
                _settings.BubbleSize = Math.Max(65, Math.Min(160, value));
                update();
                _apply();
            };
            AddCell(row, PanelTheme.Button("−", () => set(_settings.BubbleSize - 5)));
            AddBubblePreset(row, presets, "小", 80, set);
            AddBubblePreset(row, presets, "默认", 100, set);
            AddBubblePreset(row, presets, "大", 125, set);
            AddBubblePreset(row, presets, "超大", 150, set);
            AddCell(row, PanelTheme.Button("+", () => set(_settings.BubbleSize + 5)));
            Children.Add(row);
            Children.Add(preview);
            update();
        }

        private static void AddBubblePreset(UniformGrid row, Dictionary<int, Button> presets, string title, int size, Action<int> set)
        {
            var button = PanelTheme.Button(title, () => set(size));
            presets[size] = button;
            AddCell(row, button);
        }

        private void AddArt()
        {
            var reset = PanelTheme.Button("恢复默认", () => {
                _art.UseDefaults();
                UpdateArt();
                _artFeedback.Text = "已恢复内置形象";
                _apply();
            });
            Children.Add(PanelTheme.Row(PanelTheme.Text("自定义形象", 13, bold: true), reset));
            var cards = new UniformGrid { Columns = 2, Margin = new Thickness(0, 6, 0, 0) };
            AddArtCard(cards, DragonPose.Idle, "更改主形象");
            AddArtCard(cards, DragonPose.Thinking, "思考差分");
            AddArtCard(cards, DragonPose.Poke, "戳一戳差分");
            AddArtCard(cards, DragonPose.Celebrate, "彩蛋差分");
            Children.Add(cards);
            _artFeedback.Margin = new Thickness(0, 3, 0, 0);
            _artFeedback.TextWrapping = TextWrapping.Wrap;
            Children.Add(_artFeedback);
        }

        private void AddArtCard(UniformGrid row, DragonPose pose, string label)
        {
            var picture = new Image { Width = 35, Height = 35, Margin = new Thickness(0, 0, 7, 0), Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(PanelTheme.Text(label, 11.5, bold: true));
            var caption = PanelTheme.Text("", 10, PanelTheme.Muted);
            info.Children.Add(caption);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(picture);
            content.Children.Add(info);
            var button = PanelTheme.Button(label, () => PickArt(pose));
            button.Padding = new Thickness(6, 4, 6, 4);
            button.Content = content;
            AutomationProperties.SetName(button, label);
            _pictures[pose] = picture;
            _artStates[pose] = caption;
            AddCell(row, button);
        }

        private void PickArt(DragonPose pose)
        {
            var dialog = new OpenFileDialog {
                Title = "选择挂件图片", CheckFileExists = true,
                Filter = "图片|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|所有文件|*.*"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                _art.Import(pose, dialog.FileName);
                UpdateArt();
                _artFeedback.Text = "已更新形象";
                _apply();
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is FormatException || error is NotSupportedException ||
                    error is InvalidOperationException || error is UnauthorizedAccessException ||
                    error is System.Runtime.InteropServices.COMException)) throw;
                _artFeedback.Text = error is InvalidDataException ? error.Message : "图片无法读取，请确认格式或换一张 PNG。";
            }
        }

        private void UpdateArt()
        {
            foreach (var item in _pictures)
            {
                item.Value.Source = _art.Get(item.Key);
                _artStates[item.Key].Text = _art.IsCustom(item.Key) ? "自定义" :
                    _art.IsCustom(DragonPose.Idle) ? "跟随主形象" : "内置";
            }
        }

        private void AddDialogue()
        {
            var count = PanelTheme.Text("", 11, PanelTheme.Muted);
            Children.Add(PanelTheme.Row(PanelTheme.Text("自定义气泡台词", 13, bold: true), count));
            var lines = new ListBox {
                FontFamily = PanelTheme.Font, FontSize = 11, BorderBrush = PanelTheme.Line, Background = PanelTheme.Paper,
                MinHeight = 30, MaxHeight = 100, Margin = new Thickness(0, 5, 0, 0)
            };
            AutomationProperties.SetName(lines, "已添加台词");
            var entry = new TextBox {
                FontFamily = PanelTheme.Font, FontSize = 12, MaxLength = 80, Padding = new Thickness(7, 5, 7, 5),
                Margin = new Thickness(0, 0, 7, 0), BorderBrush = PanelTheme.Line,
                VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "输入台词后按 Enter 添加"
            };
            AutomationProperties.SetName(entry, "输入自定义台词");
            var note = PanelTheme.Text("可用 {quota_5h}、{quota_week}、{context_pct}、{tokens}、{model}", 10.5, PanelTheme.Muted);
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(0, 6, 0, 0);
            Button remove = null;
            Action update = () => {
                count.Text = _settings.Lines.Count + " 条";
                lines.Items.Clear();
                foreach (string line in _settings.Lines) lines.Items.Add(line);
                lines.Visibility = _settings.Lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                remove.Visibility = lines.Visibility;
                remove.IsEnabled = false;
            };
            Action add = () => {
                string value = WidgetSettings.NormalizeLine(entry.Text);
                if (_settings.Lines.Count >= 50 || value == null) return;
                _settings.Lines.Add(value);
                entry.Clear();
                update();
                _apply();
            };
            remove = PanelTheme.Button("删除选中台词", () => {
                if (lines.SelectedIndex < 0) return;
                _settings.Lines.RemoveAt(lines.SelectedIndex);
                update();
                _apply();
            });
            remove.HorizontalAlignment = HorizontalAlignment.Right;
            remove.Margin = new Thickness(0, 6, 0, 0);
            lines.SelectionChanged += (sender, args) => remove.IsEnabled = lines.SelectedIndex >= 0;
            entry.KeyDown += (sender, args) => {
                if (args.Key != System.Windows.Input.Key.Enter) return;
                add();
                args.Handled = true;
            };
            var input = PanelTheme.Row(entry, PanelTheme.Button("+ 添加台词", add, true));
            input.Margin = new Thickness(0, 8, 0, 0);
            Children.Add(input);
            Children.Add(note);
            Children.Add(lines);
            Children.Add(remove);
            update();
        }

        private void AddToggle(string title, Func<bool> read, Action<bool> change, string enabled, string disabled)
        {
            Button button = null;
            Action update = () => {
                button.Content = read() ? enabled : disabled;
                PanelTheme.Highlight(button, read());
            };
            button = PanelTheme.Button(enabled, () => {
                change(!read());
                update();
                _apply();
            });
            AutomationProperties.SetName(button, title);
            button.MinWidth = 68;
            update();
            var row = PanelTheme.Row(PanelTheme.Text(title, 12, bold: true), button);
            row.Margin = new Thickness(0, 0, 0, 6);
            Children.Add(row);
        }

        private void AddCorners()
        {
            Children.Add(PanelTheme.Row(PanelTheme.Text("快速摆放", 13, bold: true), PanelTheme.Text("拖动松手后记住位置", 11, PanelTheme.Muted)));
            var row = new UniformGrid { Columns = 4, Margin = new Thickness(0, 7, 0, 2) };
            AddCorner(row, DockCorner.BottomRight, "右下角");
            AddCorner(row, DockCorner.BottomLeft, "左下角");
            AddCorner(row, DockCorner.TopRight, "右上角");
            AddCorner(row, DockCorner.TopLeft, "左上角");
            Children.Add(row);
        }

        private void AddCorner(UniformGrid row, DockCorner corner, string title)
        {
            var button = PanelTheme.Button(title, () => {
                _settings.Corner = corner;
                _settings.Position = null;
                UpdateCorner();
                _apply();
            });
            _corners[corner] = button;
            AddCell(row, button);
        }

        private static void AddCell(UniformGrid row, Button button)
        {
            button.Margin = new Thickness(0, 0, 5, 4);
            row.Children.Add(button);
        }
    }
}
