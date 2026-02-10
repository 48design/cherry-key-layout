using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace CherryKeyLayout.Gui.Services
{
    public static class StartupHelper
    {
        private const string AppName = "CherryKeyLayoutGui";
        private const string AutoRunArgs = "--autostart --tray";
        private const string LinuxDesktopFileName = "CherryKeyLayoutGui.desktop";
        private static string ExePath => Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;

        public static void SetRunOnStartup(bool enable, int delaySeconds = 0)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(
                        "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                    if (key == null) return;

                    if (enable)
                    {
                        var value = $"\"{ExePath}\" {AutoRunArgs}";
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
                return;
            }

            if (OperatingSystem.IsLinux())
            {
                try
                {
                    var autostartPath = GetLinuxAutostartFilePath();
                    if (enable)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(autostartPath)!);
                        File.WriteAllText(autostartPath, BuildLinuxDesktopEntry(delaySeconds));
                    }
                    else if (File.Exists(autostartPath))
                    {
                        File.Delete(autostartPath);
                    }
                }
                catch { /* log or ignore */ }
            }
        }

        public static bool IsRunOnStartupEnabled()
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", false);
                if (key == null) return false;
                return key.GetValue(AppName) != null;
            }

            if (OperatingSystem.IsLinux())
            {
                return File.Exists(GetLinuxAutostartFilePath());
            }

            return false;
        }

        private static string GetLinuxAutostartFilePath()
        {
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(configHome))
            {
                configHome = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config");
            }

            return Path.Combine(configHome, "autostart", LinuxDesktopFileName);
        }

        private static string BuildLinuxDesktopEntry(int delaySeconds)
        {
            var execCommand = BuildLinuxExecCommand(delaySeconds);
            return string.Join(Environment.NewLine,
                "[Desktop Entry]",
                "Type=Application",
                $"Name={AppName}",
                $"Exec={execCommand}",
                "X-GNOME-Autostart-enabled=true",
                string.Empty);
        }

        private static string BuildLinuxExecCommand(int delaySeconds)
        {
            var baseCommand = $"\"{ExePath}\" {AutoRunArgs}";
            if (delaySeconds <= 0)
            {
                return baseCommand;
            }

            return $"sh -c \"sleep {delaySeconds}; exec {baseCommand}\"";
        }
    }
}
