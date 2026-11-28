using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

[assembly: AssemblyTitle("白龙额度 · Codex 插件")]
[assembly: AssemblyDescription("由 Codex 启动接入加载的白龙额度插件")]
[assembly: AssemblyVersion("3.2.3.0")]
[assembly: AssemblyFileVersion("3.2.3.0")]

namespace DragonQuota
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
            var settings = WidgetSettings.Load();
            if (args.Length >= 2 && args[0] == "--diagnose")
            {
                bool useNetwork = args.Length != 3 || args[2] != "--local-only";
                WriteDiagnostics(args[1], settings.Proxy, useNetwork);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--render")
            {
                RenderPreview(args[1]);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--render-panels")
            {
                RenderPanels(args[1]);
                return 0;
            }

            return 0;
        }

        private static void WriteDiagnostics(string path, string proxy, bool useNetwork)
        {
            var reading = useNetwork ? Task.Run(() => CodexUsage.ReadLiveAsync(proxy)) : Task.FromResult<QuotaSample>(null);
            var snapshot = new SessionMonitor().Read(null);
            var local = snapshot.Select(null);
            var quota = reading.GetAwaiter().GetResult() ?? snapshot.Quota;
            var result = new {
                quotaSource = quota == null ? "unavailable" : quota.FromService ? "live" : "local",
                shortWindow = SafeWindow(quota == null ? null : quota.Short),
                longWindow = SafeWindow(quota == null ? null : quota.Long),
                quotaPools = quota == null ? 0 : 1 + quota.OtherPools.Length,
                recentSessions = snapshot.Recent.Length,
                hasTokenRecord = local != null && local.ContextUsed.HasValue && local.ContextLimit.HasValue,
                turns = local == null ? 0 : local.Turns,
                toolCalls = local == null ? 0 : local.Tools,
                cacheHit = local == null ? null : local.CachePercent,
                recentTurns = local == null ? 0 : local.RecentTurns.Length,
                contextUsed = local == null ? null : local.ContextUsed,
                contextLimit = local == null ? null : local.ContextLimit,
                totalTokens = local == null ? null : local.TotalTokens
            };
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(result), new UTF8Encoding(false));
        }

        private static object SafeWindow(RateWindow rate)
        {
            if (rate == null) return null;
            return new { minutes = rate.Minutes, remaining = rate.Remaining, canShowReset = rate.ResetsAt.HasValue };
        }

        private static void RenderPreview(string path)
        {
            const int width = 382;
            const int height = 389;
            DateTimeOffset now = DateTimeOffset.Now;
            var surface = new Canvas { Width = width, Height = height, Background = WidgetView.Paint("#F7F4FA") };
            WidgetFonts.SmoothText(surface);
            var frame = new Border {
                Width = width - 18, Height = height - 17,
                BorderBrush = WidgetView.Paint("#E0D8EA"), BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(Color.FromRgb(255, 253, 255), Color.FromRgb(243, 237, 248), 112),
                CornerRadius = new CornerRadius(15)
            };
            Canvas.SetLeft(frame, 9);
            Canvas.SetTop(frame, 8);
            surface.Children.Add(frame);
            var demo = new TextBlock {
                Text = "效果预览", FontSize = 11, FontFamily = WidgetFonts.Sans,
                Foreground = WidgetView.Paint("#726481")
            };
            Canvas.SetLeft(demo, 24);
            Canvas.SetTop(demo, 20);
            surface.Children.Add(demo);
            var view = new WidgetView();
            view.ShowBubble(BubbleMode.Limits);
            view.SetLimits(new QuotaSample {
                Short = new RateWindow { Minutes = 300, Remaining = 62.4, ResetsAt = now.AddHours(2).AddMinutes(16) },
                Long = new RateWindow { Minutes = 10080, Remaining = 78.2, ResetsAt = now.AddDays(4) },
                At = now, FromService = true
            }, new LocalUsage {
                ContextUsed = 164500, ContextLimit = 272000, TotalTokens = 7216930, At = now
            }, false, now);
            Canvas.SetLeft(view, width - view.Width - 20);
            Canvas.SetTop(view, 29);
            surface.Children.Add(view);
            WritePng(surface, width, height, path);
        }

        private static void RenderPanels(string path)
        {
            const int width = 824;
            const int height = 824;
            var now = DateTimeOffset.Now;
            var sample = new LocalUsage {
                Id = "preview", Model = "GPT-6", At = now, ContextUsed = 63200, ContextLimit = 512000,
                TotalTokens = 792500, InputTokens = 782200, CacheRead = 699400,
                Output = 10300, Reasoning = 2170, Turns = 3, Tools = 18, Compactions = 1
            };
            sample.LatestCompletion = new TurnSummary { At = now, Completed = true, Seconds = 34.2, Output = 1200, Tokens = 26150, Tools = 6 };
            var quota = new QuotaSample {
                Name = "Codex", Plan = "Pro", FromService = true, At = now,
                Short = new RateWindow { Minutes = 300, Remaining = 62.4, ResetsAt = now.AddHours(2).AddMinutes(16) },
                Long = new RateWindow { Minutes = 10080, Remaining = 78.2, ResetsAt = now.AddDays(4).AddHours(3) }
            };
            var settings = new WidgetSettings();
            var art = new DragonArt(settings);
            var surface = new Canvas { Width = width, Height = height, Background = WidgetView.Paint("#F1EBF8") };
            var caption = PanelTheme.Text("白龙额度挂件  ·  功能预览", 13, PanelTheme.Muted, true);
            Canvas.SetTop(caption, 14);
            Canvas.SetLeft(caption, 17);
            surface.Children.Add(caption);
            RenderPage(surface, PanelPage.Summary, settings, art, sample, quota, now, 16);
            RenderPage(surface, PanelPage.Settings, settings, art, sample, quota, now, 420);
            WritePng(surface, width, height, path);
        }

        private static void RenderPage(Canvas surface, PanelPage page, WidgetSettings settings, DragonArt art,
            LocalUsage sample, QuotaSample quota, DateTimeOffset now, double x)
        {
            var panel = new TelemetryPanel(settings, art, () => { }, () => { }, () => { });
            panel.SetPage(page);
            panel.UpdateData(new SessionSnapshot { Recent = new[] { sample }, Quota = quota }, sample, quota, false, now);
            var root = (FrameworkElement)panel.Content;
            panel.Content = null;
            root.Width = TelemetryPanel.PanelWidth;
            root.Height = page == PanelPage.Settings ? 760 : panel.PanelHeight;
            Canvas.SetLeft(root, x);
            Canvas.SetTop(root, 43);
            surface.Children.Add(root);
        }

        private static void WritePng(FrameworkElement surface, int width, int height, string path)
        {
            surface.Measure(new Size(width, height));
            surface.Arrange(new Rect(0, 0, width, height));
            surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap(2 * width, 2 * height, 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
