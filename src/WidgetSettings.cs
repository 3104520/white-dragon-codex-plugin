using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;

namespace DragonQuota
{
    internal enum DockCorner { BottomRight, BottomLeft, TopRight, TopLeft }
    internal enum DragonPose { Idle, Thinking, Poke, Celebrate }

    internal sealed class WidgetSettings
    {
        private static readonly string DefaultFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DragonQuota.settings.json");
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private readonly string _file;

        public DockCorner Corner = DockCorner.BottomRight;
        public Point? Position;
        public int MascotSize = 122;
        public int BubbleSize = 100;
        public int Opacity = 100;
        public bool ShowStats = true;
        public bool CompletionBubble = true;
        public bool Celebrate = true;
        public int QuotaRefreshSeconds = 15;
        public string Proxy;
        public string PinnedSession;
        public readonly List<string> Lines = new List<string>();
        public readonly HashSet<DragonPose> CustomPoses = new HashSet<DragonPose>();

        public WidgetSettings(string file = null) { _file = file ?? DefaultFile; }

        public string CustomDirectory { get { return Path.Combine(Path.GetDirectoryName(_file), "custom-assets"); } }

        public static WidgetSettings Load(string file = null)
        {
            var settings = new WidgetSettings(file);
            try
            {
                if (!File.Exists(settings._file)) return settings;
                var map = CodexUsage.Parse(File.ReadAllText(settings._file, Utf8));
                DockCorner corner;
                if (Enum.TryParse(CodexUsage.GetString(map, "corner"), true, out corner) && Enum.IsDefined(typeof(DockCorner), corner))
                    settings.Corner = corner;
                var position = CodexUsage.GetMap(map, "position");
                var x = CodexUsage.GetNumber(position, "x");
                var y = CodexUsage.GetNumber(position, "y");
                if (x >= 0 && x < 100000 && y >= 0 && y < 100000)
                    settings.Position = new Point(x.Value, y.Value);
                long? legacy = CodexUsage.GetInt(map, "size");
                int fallback = legacy == 82 || legacy == 100 || legacy == 118 ? (int)Math.Round(149 * legacy.Value / 100.0) : settings.MascotSize;
                settings.MascotSize = ReadRange(map, "mascotSize", fallback, 64, 180);
                settings.BubbleSize = ReadRange(map, "bubbleSize", 100, 65, 160);
                settings.Opacity = ReadRange(map, "opacity", 100, 35, 100);
                settings.ShowStats = ReadBool(map, "showStats");
                settings.CompletionBubble = ReadBool(map, "completionBubble");
                settings.Celebrate = ReadBool(map, "celebrate");
                var interval = CodexUsage.GetInt(map, "quotaRefreshSeconds");
                if (interval == 10 || interval == 15 || interval == 60) settings.QuotaRefreshSeconds = (int)interval.Value;
                settings.Proxy = CleanProxy(CodexUsage.GetString(map, "proxy"));
                var id = CodexUsage.GetString(map, "pinnedSession");
                if (id != null && id.Length <= 120) settings.PinnedSession = id;
                foreach (object item in CodexUsage.GetValue(map, "lines") as object[] ?? new object[0])
                {
                    var line = NormalizeLine(item as string);
                    if (line != null && settings.Lines.Count < 50) settings.Lines.Add(line);
                }
                foreach (object item in CodexUsage.GetValue(map, "customPoses") as object[] ?? new object[0])
                {
                    DragonPose pose;
                    if (Enum.TryParse(item as string, true, out pose) && Enum.IsDefined(typeof(DragonPose), pose))
                        settings.CustomPoses.Add(pose);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return settings;
        }

        public bool Save()
        {
            var custom = new List<string>();
            foreach (var pose in CustomPoses) custom.Add(pose.ToString());
            var options = new Dictionary<string, object> {
                { "corner", Corner.ToString() }, { "mascotSize", MascotSize }, { "bubbleSize", BubbleSize }, { "opacity", Opacity },
                { "showStats", ShowStats }, { "completionBubble", CompletionBubble }, { "celebrate", Celebrate },
                { "quotaRefreshSeconds", QuotaRefreshSeconds },
                { "lines", Lines }, { "customPoses", custom }
            };
            if (Proxy != null) options["proxy"] = Proxy;
            if (PinnedSession != null) options["pinnedSession"] = PinnedSession;
            if (Position.HasValue) options["position"] = new { x = Position.Value.X, y = Position.Value.Y };
            string staging = _file + ".tmp";
            try
            {
                File.WriteAllText(staging, new JavaScriptSerializer().Serialize(options) + Environment.NewLine, Utf8);
                if (File.Exists(_file)) File.Replace(staging, _file, null);
                else File.Move(staging, _file);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            finally
            {
                try { if (File.Exists(staging)) File.Delete(staging); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public static string NormalizeLine(string line)
        {
            if (String.IsNullOrWhiteSpace(line)) return null;
            line = line.Trim();
            return line.Length <= 80 ? line : line.Substring(0, 80);
        }

        private static int ReadRange(Dictionary<string, object> map, string key, int fallback, int minimum, int maximum)
        {
            long? value = CodexUsage.GetInt(map, key);
            return value.HasValue ? (int)Math.Max(minimum, Math.Min(maximum, value.Value)) : fallback;
        }

        private static bool ReadBool(Dictionary<string, object> map, string key)
        {
            var value = CodexUsage.GetValue(map, key);
            return !(value is bool) || (bool)value;
        }

        public static string CleanProxy(string text)
        {
            Uri address;
            if (!Uri.TryCreate(text, UriKind.Absolute, out address)) return null;
            if (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps) return null;
            if (address.UserInfo.Length > 0 || address.Host.Length == 0) return null;
            if (address.AbsolutePath != "/" || address.Query.Length > 0 || address.Fragment.Length > 0) return null;
            return address.GetLeftPart(UriPartial.Authority);
        }
    }
}
