using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using DragonCodexBoot;

namespace WhiteDragonStartup
{
    internal static class Program
    {
        private static readonly string[] DesktopProcessNames = { "ChatGPT", "Codex" };

        private static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--check") return 0;
            if (args.Length != 0) return 2;

            bool firstInstance;
            using (var mutex = new Mutex(true, @"Local\WhiteDragonCodexPlugin.Startup", out firstInstance))
            {
                if (!firstInstance) return 0;

                string root = AppDomain.CurrentDomain.BaseDirectory;
                Directory.CreateDirectory(Path.Combine(root, "logs"));
                using (var plugin = new WhiteDragonPlugin(root))
                {
                    while (true)
                    {
                        plugin.Update(CodexWindowExists());
                        Thread.Sleep(750);
                    }
                }
            }
        }

        private static bool CodexWindowExists()
        {
            bool found = false;
            foreach (string name in DesktopProcessNames)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        if (found) continue;
                        try
                        {
                            found = process.MainWindowHandle != IntPtr.Zero
                                && process.MainModule.FileName.IndexOf(@"\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0;
                        }
                        catch (Win32Exception) { }
                        catch (InvalidOperationException) { }
                    }
                }
            }
            return found;
        }
    }
}
