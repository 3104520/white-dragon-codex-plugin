using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace DragonQuota
{
    internal sealed class DragonArt
    {
        private readonly WidgetSettings _settings;
        private readonly Dictionary<DragonPose, BitmapSource> _builtIn = new Dictionary<DragonPose, BitmapSource>();
        private readonly Dictionary<DragonPose, BitmapSource> _custom = new Dictionary<DragonPose, BitmapSource>();

        public DragonArt(WidgetSettings settings)
        {
            _settings = settings;
            foreach (DragonPose pose in Enum.GetValues(typeof(DragonPose)))
            {
                string resource = pose == DragonPose.Idle ? "DragonQuota.Dragon.png" : "DragonQuota." + pose.ToString().ToLowerInvariant() + ".png";
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                {
                    if (stream != null) _builtIn[pose] = Decode(stream);
                }
                if (!settings.CustomPoses.Contains(pose)) continue;
                try
                {
                    using (var stream = File.OpenRead(PathFor(pose))) _custom[pose] = Decode(stream);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (NotSupportedException) { }
                catch (System.IO.FileFormatException) { }
                catch (InvalidOperationException) { }
            }
        }

        public bool IsCustom(DragonPose pose) { return _custom.ContainsKey(pose); }

        public BitmapSource Get(DragonPose pose)
        {
            BitmapSource sprite;
            if (_custom.TryGetValue(pose, out sprite) || _custom.TryGetValue(DragonPose.Idle, out sprite) ||
                _builtIn.TryGetValue(pose, out sprite)) return sprite;
            return _builtIn[DragonPose.Idle];
        }

        public void Import(DragonPose pose, string path)
        {
            var info = new FileInfo(path);
            if (info.Length > 24 * 1024 * 1024) throw new InvalidDataException("图片大小请控制在 24 MB 内。");
            BitmapSource sprite;
            using (var input = info.OpenRead()) sprite = Decode(input);
            if (sprite.PixelHeight > 4096) throw new InvalidDataException("这张图片过高，请裁剪后再导入。");

            Directory.CreateDirectory(_settings.CustomDirectory);
            string destination = PathFor(pose);
            string staged = destination + ".tmp";
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(sprite));
                using (var output = File.Create(staged)) encoder.Save(output);
                if (File.Exists(destination)) File.Replace(staged, destination, null);
                else File.Move(staged, destination);
            }
            finally
            {
                if (File.Exists(staged)) File.Delete(staged);
            }
            _custom[pose] = sprite;
            _settings.CustomPoses.Add(pose);
        }

        public void UseDefaults()
        {
            _custom.Clear();
            _settings.CustomPoses.Clear();
        }

        private string PathFor(DragonPose pose)
        {
            return Path.Combine(_settings.CustomDirectory, pose.ToString().ToLowerInvariant() + ".png");
        }

        private static BitmapSource Decode(Stream input)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = input;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 600;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
