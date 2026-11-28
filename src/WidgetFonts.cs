using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace DragonQuota
{
    internal static class WidgetFonts
    {
        private const string RoundedFile = "ResourceHanRoundedCN-Regular.ttf";
        private static readonly Uri DirectoryUri = FindRoundedFont();

        public static readonly FontFamily Sans = Choose("Anthropic Sans Desktop, Anthropic Sans Web Display Light", "Segoe UI");
        public static readonly FontFamily Serif = Choose("Anthropic Serif Desktop, Anthropic Serif Web Display Light", "Georgia");
        public static readonly FontFamily Mono = Choose("Anthropic Mono Desktop, Anthropic Mono Web", "Consolas");

        public static void SmoothText(DependencyObject surface)
        {
            TextOptions.SetTextFormattingMode(surface, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(surface, TextRenderingMode.Grayscale);
        }

        private static FontFamily Choose(string latin, string fallback)
        {
            string families = latin + ", Noto Sans SC, Microsoft YaHei UI, " + fallback;
            if (DirectoryUri == null) return new FontFamily(families);
            return new FontFamily(DirectoryUri, latin + ", ./#Resource Han Rounded CN, Noto Sans SC, Microsoft YaHei UI, " + fallback);
        }

        private static Uri FindRoundedFont()
        {
            // The Windows startup monitor loads the plugin assembly from bytes.
            var direct = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "fonts");
            if (File.Exists(Path.Combine(direct, RoundedFile))) return FolderUri(direct);

            string root = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (String.IsNullOrWhiteSpace(root))
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            string version = typeof(WidgetFonts).Assembly.GetName().Version.ToString(3);
            string installed = Path.Combine(root, "plugins", "cache", "white-dragon-local", "white-dragon", version, "assets", "fonts");
            return File.Exists(Path.Combine(installed, RoundedFile)) ? FolderUri(installed) : null;
        }

        private static Uri FolderUri(string folder)
        {
            return new Uri(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, UriKind.Absolute);
        }
    }
}
