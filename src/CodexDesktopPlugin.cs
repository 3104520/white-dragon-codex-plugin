using System;
using System.Threading;
using System.Windows.Threading;

namespace DragonQuota
{
    public static class CodexDesktopPlugin
    {
        public static IDisposable Start(string settingsPath)
        {
            if (String.IsNullOrWhiteSpace(settingsPath)) throw new ArgumentException("A plugin settings path is required.");
            return new Session(settingsPath);
        }

        private sealed class Session : IDisposable
        {
            private readonly Thread _thread;
            private readonly string _settingsPath;
            private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
            private Dispatcher _dispatcher;
            private WidgetWindow _window;
            private Exception _failure;
            private int _stopping;

            public Session(string settingsPath)
            {
                _settingsPath = settingsPath;
                _thread = new Thread(Run) { Name = "White Dragon Codex plugin", IsBackground = true };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                if (!_ready.Wait(5000))
                {
                    Dispose();
                    throw new TimeoutException("The white dragon plugin window did not initialize.");
                }
                if (_failure != null) throw new InvalidOperationException("The white dragon plugin window did not start.", _failure);
            }

            private void Run()
            {
                try
                {
                    AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
                    _dispatcher = Dispatcher.CurrentDispatcher;
                    _window = new WidgetWindow(WidgetSettings.Load(_settingsPath));
                    _ready.Set();
                    if (Volatile.Read(ref _stopping) == 0) Dispatcher.Run();
                }
                catch (Exception error)
                {
                    _failure = error;
                    _ready.Set();
                }
                finally
                {
                    try { CloseWindow(); }
                    catch (InvalidOperationException) { }
                }
            }

            private void CloseWindow()
            {
                var window = _window;
                _window = null;
                if (window != null) window.Close();
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
                var dispatcher = _dispatcher;
                if (dispatcher != null && !dispatcher.HasShutdownStarted)
                    dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => {
                        try { CloseWindow(); }
                        finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal); }
                    }));
                _thread.Join(2500);
            }
        }
    }
}
