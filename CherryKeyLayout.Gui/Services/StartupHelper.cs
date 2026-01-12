using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace CherryKeyLayout.Gui.Services
{
    public static class StartupHelper
    {
        private const string AppName = "CherryKeyLayoutGui";
        private static string ExePath => Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;

        public static void SetRunOnStartup(bool enable, int delaySeconds = 0)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (key == null) return;

                if (enable)
                {
                    var value = $"\"{ExePath}\"";
                    if (delaySeconds > 0)
                    {
                        value = $"cmd /c timeout /t {delaySeconds} && {value}";
                    }
                    key.SetValue(AppName, value);
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch { /* log or ignore */ }
        }

        public static bool IsRunOnStartupEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", false);
            if (key == null) return false;
            return key.GetValue(AppName) != null;
        }
    }
}
