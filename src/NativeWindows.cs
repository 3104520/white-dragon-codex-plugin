using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DragonQuota
{
    internal static class NativeWindows
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Bounds
        {
            public int Left, Top, Right, Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorPosition { public int X, Y; }

        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int maxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Bounds bounds);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPosition position);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(CursorPosition position);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Bounds bounds, int size);

        public static bool IsChatGpt(IntPtr window)
        {
            if (window == IntPtr.Zero || GetWindowTextLength(window) == 0) return false;
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            if (processId == 0 || processId > Int32.MaxValue) return false;

            try
            {
                using (var process = Process.GetProcessById((int)processId))
                {
                    if (String.Equals(process.ProcessName, "ChatGPT", StringComparison.OrdinalIgnoreCase)) return true;
                    if (!String.Equals(process.ProcessName, "Codex", StringComparison.OrdinalIgnoreCase)) return false;
                    var kind = new StringBuilder(80);
                    GetClassName(window, kind, kind.Capacity);
                    return kind.ToString().StartsWith("Chrome_WidgetWin", StringComparison.Ordinal);
                }
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }

        public static bool TryGetBounds(IntPtr window, out Bounds bounds)
        {
            bounds = new Bounds();
            if (window == IntPtr.Zero || !IsWindowVisible(window) || IsIconic(window)) return false;
            const int extendedFrameBounds = 9;
            if (DwmGetWindowAttribute(window, extendedFrameBounds, out bounds, Marshal.SizeOf(typeof(Bounds))) != 0 &&
                !GetWindowRect(window, out bounds)) return false;
            return bounds.Width > 0 && bounds.Height > 0;
        }

        public static double ScaleOf(IntPtr window)
        {
            if (window == IntPtr.Zero) return 1.0;
            uint dpi = GetDpiForWindow(window);
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }

        public static bool BelongsTo(IntPtr window, int expectedProcessId)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            return owner == (uint)expectedProcessId;
        }

        public static bool TryGetPointer(out System.Windows.Point pointer, out bool pressed)
        {
            CursorPosition position;
            bool found = GetCursorPos(out position);
            pointer = new System.Windows.Point(position.X, position.Y);
            int primaryButton = GetSystemMetrics(23) == 0 ? 1 : 2;
            pressed = (GetAsyncKeyState(primaryButton) & 0x8000) != 0;
            return found;
        }

        public static bool OtherMouseButtonDown()
        {
            int secondaryButton = GetSystemMetrics(23) == 0 ? 2 : 1;
            return (GetAsyncKeyState(secondaryButton) & 0x8000) != 0;
        }

        public static IntPtr WindowAt(System.Windows.Point point)
        {
            return WindowFromPoint(new CursorPosition { X = (int)point.X, Y = (int)point.Y });
        }

        public static void KeepKeyboardOnHost(IntPtr window, bool clickThrough = false)
        {
            const int extendedStyle = -20;
            const int noActivate = 0x08000000;
            const int toolWindow = 0x00000080;
            const int transparent = 0x00000020;
            int style = GetWindowLong(window, extendedStyle);
            if (clickThrough) style |= transparent;
            SetWindowLong(window, extendedStyle, style | noActivate | toolWindow);
        }

        public static void Place(IntPtr window, int x, int y, int width, int height, bool show = false)
        {
            const uint noActivate = 0x0010;
            const uint noOwnerZOrder = 0x0200;
            const uint showWindow = 0x0040;
            uint flags = noActivate | noOwnerZOrder;
            if (show) flags |= showWindow;
            SetWindowPos(window, new IntPtr(-1), x, y, width, height, flags);
        }

        public static void Move(IntPtr window, int x, int y)
        {
            const uint noSize = 0x0001;
            const uint noZOrder = 0x0004;
            const uint noActivate = 0x0010;
            const uint noOwnerZOrder = 0x0200;
            const uint noChanging = 0x0400;
            SetWindowPos(window, IntPtr.Zero, x, y, 0, 0, noSize | noZOrder | noActivate | noOwnerZOrder | noChanging);
        }
    }
}
