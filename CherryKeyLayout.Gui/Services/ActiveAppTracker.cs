using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CherryKeyLayout.Gui.Services
{
    internal static class ActiveAppTracker
    {
        public static string? GetActiveProcessPath()
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            if (GetWindowThreadProcessId(hwnd, out var processId) == 0 || processId == 0)
            {
                return null;
            }

            try
            {
                using var process = Process.GetProcessById((int)processId);
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }

        public static string? GetCurrentProcessPath()
        {
            try
            {
                return Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath;
            }
            catch
            {
                return null;
            }
        }

        public static string? GetCurrentProcessName()
        {
            try
            {
                return Process.GetCurrentProcess().ProcessName;
            }
            catch
            {
                return null;
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
