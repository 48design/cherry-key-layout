using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Avalonia.Threading;

namespace CherryKeyLayout.Gui.Services
{
    internal sealed class ScreenSaverWatcher : IDisposable
    {
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(1);
        private readonly SemaphoreSlim _gate = new(1, 1);
        private Timer? _timer;
        private Timer? _monitorOffClearTimer;
        private WindowsSignalWindow? _signalWindow;
        private bool _screenSaverActive;
        private bool _monitorOff;
        private bool _sessionLocked;
        private bool _lastState;
        private DateTime _lastMonitorOffChange = DateTime.MinValue;

        public event Action<bool>? ScreenSaverStateChanged;

        public void Start()
        {
            if (_timer != null)
            {
                return;
            }

            _timer = new Timer(_ => Tick(), null, _interval, _interval);
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    _signalWindow = new WindowsSignalWindow(OnDisplayPowerChanged, OnSessionLockChanged);
                }
                catch (Exception ex)
                {
                    WindowsSignalWindow.LogSignalFailure("CreateWindow", ex);
                    _signalWindow = null;
                }
            }
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
            _monitorOffClearTimer?.Dispose();
            _monitorOffClearTimer = null;
            if (OperatingSystem.IsWindows())
            {
                _signalWindow?.Dispose();
            }
            _signalWindow = null;
            _screenSaverActive = false;
            _monitorOff = false;
            _sessionLocked = false;
            _lastState = false;
        }

        public void Dispose()
        {
            Stop();
            _gate.Dispose();
        }

        private void Tick()
        {
            if (!_gate.Wait(0))
            {
                return;
            }

            try
            {
                var isActive = ScreenSaverStateProvider.IsScreenSaverRunning();
                if (isActive == _screenSaverActive)
                {
                    return;
                }

                _screenSaverActive = isActive;
                UpdateActiveState();
            }
            finally
            {
                _gate.Release();
            }
        }

        private void OnDisplayPowerChanged(bool isOffOrDim)
        {
            UpdateMonitorState(isOffOrDim);
        }

        private void OnSessionLockChanged(bool isLocked)
        {
            UpdateSignalState(() => _sessionLocked = isLocked);
        }

        private void UpdateSignalState(Action update)
        {
            if (!_gate.Wait(0))
            {
                return;
            }

            try
            {
                update();
                UpdateActiveState();
            }
            finally
            {
                _gate.Release();
            }
        }

        private void UpdateMonitorState(bool isOffOrDim)
        {
            if (!_gate.Wait(0))
            {
                return;
            }

            try
            {
                if (isOffOrDim)
                {
                    _monitorOffClearTimer?.Dispose();
                    _monitorOffClearTimer = null;
                    _monitorOff = true;
                    _lastMonitorOffChange = DateTime.UtcNow;
                    UpdateActiveState();
                    return;
                }

                if (_monitorOff)
                {
                    var elapsed = DateTime.UtcNow - _lastMonitorOffChange;
                    if (elapsed < TimeSpan.FromSeconds(5))
                    {
                        var due = TimeSpan.FromSeconds(5) - elapsed;
                        _monitorOffClearTimer?.Dispose();
                        _monitorOffClearTimer = new Timer(_ => ClearMonitorOff(), null, due, Timeout.InfiniteTimeSpan);
                        return;
                    }
                }

                _monitorOff = false;
                UpdateActiveState();
            }
            finally
            {
                _gate.Release();
            }
        }

        private void ClearMonitorOff()
        {
            if (!_gate.Wait(0))
            {
                return;
            }

            try
            {
                if (!_monitorOff)
                {
                    return;
                }

                _monitorOff = false;
                UpdateActiveState();
            }
            finally
            {
                _gate.Release();
            }
        }

        private void UpdateActiveState()
        {
            var isActive = _screenSaverActive || _monitorOff || _sessionLocked;
            if (isActive == _lastState)
            {
                return;
            }

            _lastState = isActive;
            if (OperatingSystem.IsWindows())
            {
                WindowsSignalWindow.LogSignalEvent(
                    $"StateChanged active={isActive} screensaver={_screenSaverActive} monitorOff={_monitorOff} sessionLocked={_sessionLocked}");
            }
            if (Dispatcher.UIThread.CheckAccess())
            {
                ScreenSaverStateChanged?.Invoke(isActive);
            }
            else
            {
                Dispatcher.UIThread.Post(() => ScreenSaverStateChanged?.Invoke(isActive));
            }
        }
    }

    internal static class ScreenSaverStateProvider
    {
        private const uint SpiGetScreenSaverRunning = 0x0072;

        public static bool IsScreenSaverRunning()
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            var running = 0;
            if (!SystemParametersInfo(SpiGetScreenSaverRunning, 0, ref running, 0))
            {
                return false;
            }

            return running != 0;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(
            uint uiAction,
            uint uiParam,
            ref int pvParam,
            uint fWinIni);
    }

    [SupportedOSPlatform("windows")]
    internal sealed class WindowsSignalWindow : IDisposable
    {
        private const int WmPowerBroadcast = 0x218;
        private const int PbtPowerSettingChange = 0x8013;
        private const int WmWtsSessionChange = 0x02B1;
        private const int WtsSessionLock = 0x7;
        private const int WtsSessionUnlock = 0x8;
        private const int DeviceNotifyWindowHandle = 0x00000000;
        private const int NotifyForThisSession = 0x0;
        private static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");
        private static readonly IntPtr HwndMessage = new(-3);

        private readonly Action<bool> _displayPowerChanged;
        private readonly Action<bool> _sessionLockChanged;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new(false);
        private readonly ManualResetEventSlim _stopped = new(false);
        private readonly WindowProc _wndProc;
        private IntPtr _hwnd;
        private IntPtr _powerNotifyHandle;
        private IntPtr _wtsLib;
        private WtsRegisterSessionNotificationDelegate? _wtsRegister;
        private WtsUnregisterSessionNotificationDelegate? _wtsUnregister;
        private bool _sessionNotifyRegistered;
        private string _className = string.Empty;

        public WindowsSignalWindow(Action<bool> displayPowerChanged, Action<bool> sessionLockChanged)
        {
            _displayPowerChanged = displayPowerChanged ?? throw new ArgumentNullException(nameof(displayPowerChanged));
            _sessionLockChanged = sessionLockChanged ?? throw new ArgumentNullException(nameof(sessionLockChanged));
            _wndProc = WndProc;
            _thread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = "CherryKeyLayout.SignalWindow"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.Wait();
        }

        public void Dispose()
        {
            if (_hwnd != IntPtr.Zero)
            {
                PostMessage(_hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
            }

            _stopped.Wait(TimeSpan.FromSeconds(2));
            _ready.Dispose();
            _stopped.Dispose();
        }

        private void RunLoop()
        {
            try
            {
                _className = "CherryKeyLayout.SignalWindow." + Guid.NewGuid().ToString("N");
                var wndClass = new WndClass
                {
                    lpszClassName = _className,
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                    hInstance = GetModuleHandle(null)
                };

                if (RegisterClass(ref wndClass) == 0)
                {
                    _ready.Set();
                    _stopped.Set();
                    return;
                }

                _hwnd = CreateWindowEx(
                    0,
                    _className,
                    string.Empty,
                    0,
                    0,
                    0,
                    0,
                    0,
                    HwndMessage,
                    IntPtr.Zero,
                    wndClass.hInstance,
                    IntPtr.Zero);

                if (_hwnd == IntPtr.Zero)
                {
                    _ready.Set();
                    _stopped.Set();
                    return;
                }

                var displayGuid = ConsoleDisplayState;
                _powerNotifyHandle = RegisterPowerSettingNotification(
                    _hwnd,
                    ref displayGuid,
                    DeviceNotifyWindowHandle);

                TryRegisterSessionNotifications();
                _ready.Set();

                while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            catch
            {
                _ready.Set();
            }
            finally
            {
                if (_powerNotifyHandle != IntPtr.Zero)
                {
                    UnregisterPowerSettingNotification(_powerNotifyHandle);
                    _powerNotifyHandle = IntPtr.Zero;
                }

                if (_hwnd != IntPtr.Zero)
                {
                    TryUnregisterSessionNotifications();
                    DestroyWindow(_hwnd);
                    _hwnd = IntPtr.Zero;
                }

                _stopped.Set();
            }
        }

        private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (msg == WmPowerBroadcast && wParam.ToInt64() == PbtPowerSettingChange)
                {
                    HandlePowerBroadcast(lParam);
                    return IntPtr.Zero;
                }

                if (msg == WmWtsSessionChange)
                {
                    var reason = wParam.ToInt32();
                    if (reason == WtsSessionLock)
                    {
                        _sessionLockChanged(true);
                    }
                    else if (reason == WtsSessionUnlock)
                    {
                        _sessionLockChanged(false);
                    }
                }

                if (msg == 0x0010) // WM_CLOSE
                {
                    DestroyWindow(hwnd);
                    PostQuitMessage(0);
                    return IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                LogSignalFailure("WndProc", ex);
            }

            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        private void HandlePowerBroadcast(IntPtr lParam)
        {
            if (lParam == IntPtr.Zero)
            {
                return;
            }

            try
            {
                var setting = Marshal.PtrToStructure<PowerBroadcastSetting>(lParam);
                if (setting.PowerSetting != ConsoleDisplayState)
                {
                    return;
                }

                var dataLength = setting.DataLength;
                if (dataLength <= 0)
                {
                    return;
                }

                var dataOffset = Marshal.OffsetOf<PowerBroadcastSetting>(nameof(PowerBroadcastSetting.Data)).ToInt32();
                var value = dataLength >= 4
                    ? Marshal.ReadInt32(lParam, dataOffset)
                    : Marshal.ReadByte(lParam, dataOffset);
                var isOffOrDim = value == 0 || value == 2;
                LogSignalEvent($"DisplayPowerChanged value={value} offOrDim={isOffOrDim}");
                _displayPowerChanged(isOffOrDim);
            }
            catch (Exception ex)
            {
                LogSignalFailure("HandlePowerBroadcast", ex);
            }
        }

        private delegate IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        private void TryRegisterSessionNotifications()
        {
            _wtsLib = LoadLibrary("wtsapi32.dll");
            if (_wtsLib == IntPtr.Zero)
            {
                return;
            }

            var registerPtr = GetProcAddress(_wtsLib, "WTSRegisterSessionNotification");
            var unregisterPtr = GetProcAddress(_wtsLib, "WTSUnRegisterSessionNotification");
            if (registerPtr == IntPtr.Zero || unregisterPtr == IntPtr.Zero)
            {
                return;
            }

            _wtsRegister = Marshal.GetDelegateForFunctionPointer<WtsRegisterSessionNotificationDelegate>(registerPtr);
            _wtsUnregister = Marshal.GetDelegateForFunctionPointer<WtsUnregisterSessionNotificationDelegate>(unregisterPtr);
            try
            {
                _sessionNotifyRegistered = _wtsRegister(_hwnd, NotifyForThisSession);
            }
            catch
            {
                _sessionNotifyRegistered = false;
            }
        }

        private void TryUnregisterSessionNotifications()
        {
            if (_sessionNotifyRegistered && _wtsUnregister != null)
            {
                try
                {
                    _wtsUnregister(_hwnd);
                }
                catch
                {
                }
            }

            _sessionNotifyRegistered = false;
            _wtsRegister = null;
            _wtsUnregister = null;

            if (_wtsLib != IntPtr.Zero)
            {
                FreeLibrary(_wtsLib);
                _wtsLib = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WndClass
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PowerBroadcastSetting
        {
            public Guid PowerSetting;
            public int DataLength;
            public byte Data;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClass([In] ref WndClass lpWndClass);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle,
            string lpClassName,
            string lpWindowName,
            int dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern void PostQuitMessage(int nExitCode);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage([In] ref Msg lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage([In] ref Msg lpMsg);

        [StructLayout(LayoutKind.Sequential)]
        private struct Msg
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr RegisterPowerSettingNotification(
            IntPtr hRecipient,
            [In] ref Guid PowerSettingGuid,
            int Flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate bool WtsRegisterSessionNotificationDelegate(IntPtr hWnd, int dwFlags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate bool WtsUnregisterSessionNotificationDelegate(IntPtr hWnd);

        internal static void LogSignalFailure(string source, Exception exception)
        {
            try
            {
                var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var logDir = Path.Combine(basePath, "CherryKeyLayout");
                Directory.CreateDirectory(logDir);
                var logPath = Path.Combine(logDir, "crash.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WindowsSignalWindow.{source}\n{exception}\n");
            }
            catch
            {
            }
        }

        internal static void LogSignalEvent(string message)
        {
            try
            {
                var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var logDir = Path.Combine(basePath, "CherryKeyLayout");
                Directory.CreateDirectory(logDir);
                var logPath = Path.Combine(logDir, "crash.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WindowsSignalWindow {message}\n");
            }
            catch
            {
            }
        }
    }
}
