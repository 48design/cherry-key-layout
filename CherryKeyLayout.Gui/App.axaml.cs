using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Platform;
using System.Text;

namespace CherryKeyLayout.Gui
{
    public sealed partial class App : Application
    {
        private TrayIcon? _trayIcon;
        private bool _isExiting;
        private IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
        private IDisposable? _windowStateSubscription;
        private bool _suppressTrayClick;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _desktopLifetime = desktop;
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                    LogUnhandledException(args.ExceptionObject as Exception, "AppDomain.UnhandledException");
                Dispatcher.UIThread.UnhandledException += (_, args) =>
                {
                    LogUnhandledException(args.Exception, "Dispatcher.UIThread.UnhandledException");
                    ShowCrashDialog(args.Exception);
                    args.Handled = true;
                };

                var mainWindow = new MainWindow();
                desktop.MainWindow = mainWindow;
                mainWindow.Closing += OnMainWindowClosing;
                _windowStateSubscription = mainWindow.GetObservable(Window.WindowStateProperty)
                    .Subscribe(new WindowStateObserver(state => OnWindowStateChanged(mainWindow, state)));

                CreateTrayIcon(mainWindow, desktop);
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void OnMainWindowClosing(object? sender, CancelEventArgs e)
        {
            if (_isExiting)
            {
                return;
            }

            e.Cancel = true;
            if (sender is Window window && _desktopLifetime != null)
            {
                _ = ConfirmExitAsync(window, _desktopLifetime);
            }
        }

        private void OnWindowStateChanged(Window window, WindowState state)
        {
            if (_isExiting)
            {
                return;
            }

            if (state == WindowState.Minimized)
            {
                HideToTray(window);
                return;
            }
        }

        private void CreateTrayIcon(Window window, IClassicDesktopStyleApplicationLifetime desktop)
        {
            var menu = new NativeMenu();
            var openItem = new NativeMenuItem("Open");
            openItem.Click += (_, __) => ShowWindow(window);
            var exitItem = new NativeMenuItem("Exit");
            exitItem.Click += (_, __) => ExitApp(desktop, window);

            menu.Items.Add(openItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(exitItem);
            menu.Opening += (_, __) =>
            {
                _suppressTrayClick = true;
                DispatcherTimer.RunOnce(() => _suppressTrayClick = false, TimeSpan.FromMilliseconds(250));
            };

            var iconBitmap = LoadTrayIcon();

                _trayIcon = new TrayIcon
                {
                    Icon = new WindowIcon(iconBitmap),
                    ToolTipText = "Cherry Key Layout",
                    Menu = menu,
                    IsVisible = true
                };
                _trayIcon.Clicked += (_, __) =>
                {
                    if (_suppressTrayClick)
                    {
                        return;
                    }

                    ShowWindow(window);
                };
            }

        private static Bitmap LoadTrayIcon()
        {
            var iconUri = new Uri("avares://CherryKeyLayout.Gui/Assets/tray-icon.png");
            try
            {
                using var iconStream = AssetLoader.Open(iconUri);
                return new Bitmap(iconStream);
            }
            catch
            {
                var fallbackPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tray-icon.png");
                if (File.Exists(fallbackPath))
                {
                    return new Bitmap(fallbackPath);
                }

                return CreateFallbackBitmap();
            }
        }

        private static Bitmap CreateFallbackBitmap()
        {
            const string base64Png =
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGMAAQAABQABDQottAAAAABJRU5ErkJggg==";
            var bytes = Convert.FromBase64String(base64Png);
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }

        private static string GetCrashLogPath()
        {
            var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDir = Path.Combine(basePath, "CherryKeyLayout");
            Directory.CreateDirectory(logDir);
            return Path.Combine(logDir, "crash.log");
        }

        private static void LogUnhandledException(Exception? exception, string source)
        {
            try
            {
                var logPath = GetCrashLogPath();
                var builder = new StringBuilder();
                builder.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}");
                builder.AppendLine(exception?.ToString() ?? "Unknown exception");
                builder.AppendLine(new string('-', 60));
                File.AppendAllText(logPath, builder.ToString());
            }
            catch
            {
            }
        }

        private void ShowCrashDialog(Exception? exception)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var logPath = GetCrashLogPath();
                var message = $"The app hit an unexpected error and stayed open.\n\n{exception}\n\nLog: {logPath}";
                var dialog = new Window
                {
                    Title = "Cherry Key Layout - Error",
                    Width = 720,
                    Height = 420,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new TextBox
                    {
                        Text = message,
                        IsReadOnly = true,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    }
                };

                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                {
                    dialog.ShowDialog(desktop.MainWindow);
                }
                else
                {
                    dialog.Show();
                }
            });
        }

        private void ShowWindow(Window window)
        {
            Dispatcher.UIThread.Post(() =>
            {
                window.ShowInTaskbar = true;
                if (!window.IsVisible)
                {
                    window.Show();
                }

                window.WindowState = WindowState.Normal;
                window.ShowInTaskbar = true;
                window.Activate();
                window.Focus();
                window.Topmost = true;
                window.Topmost = false;
            });
        }

        private void HideToTray(Window window)
        {
            window.WindowState = WindowState.Normal;
            window.ShowInTaskbar = false;
            window.Hide();
        }

        private void ExitApp(IClassicDesktopStyleApplicationLifetime desktop, Window window)
        {
            _isExiting = true;
            _windowStateSubscription?.Dispose();
            _trayIcon?.Dispose();
            window.Close();
            desktop.Shutdown();
        }

        private async Task ConfirmExitAsync(Window owner, IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dialog = new ExitDialogWindow
            {
                Icon = owner.Icon,
                Topmost = true,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = owner.Position,
                Width = owner.Bounds.Width,
                Height = owner.Bounds.Height
            };

            var result = await dialog.ShowDialog<ExitDialogWindow.ExitDialogResult>(owner);
            if (result == ExitDialogWindow.ExitDialogResult.Exit)
            {
                ExitApp(desktop, owner);
            }
            else if (result == ExitDialogWindow.ExitDialogResult.Minimize)
            {
                HideToTray(owner);
            }
        }

        private sealed class WindowStateObserver : IObserver<WindowState>
        {
            private readonly Action<WindowState> _onNext;

            public WindowStateObserver(Action<WindowState> onNext)
            {
                _onNext = onNext ?? throw new ArgumentNullException(nameof(onNext));
            }

            public void OnCompleted()
            {
            }

            public void OnError(Exception error)
            {
            }

            public void OnNext(WindowState value)
            {
                _onNext(value);
            }
        }
    }
}
