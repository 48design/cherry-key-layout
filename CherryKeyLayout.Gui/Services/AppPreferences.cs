using System;
using System.IO;
using System.Text.Json;

namespace CherryKeyLayout.Gui.Services
{
    internal sealed class AppPreferences
    {
        public string? SettingsPath { get; set; }
        public int? DefaultProfileIndex { get; set; }
        public string? DeviceName { get; set; }
        public string? KeyboardImagePath { get; set; }
        public string? KeyboardLayoutPath { get; set; }
        public string? SelectedDeviceId { get; set; }
        public bool RunOnSystemStart { get; set; }
        public int StartupDelaySeconds { get; set; }
        public DeviceConfig[] Devices { get; set; } = Array.Empty<DeviceConfig>();

        public static AppPreferences Load()
        {
            var path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return new AppPreferences();
            }

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppPreferences>(json) ?? new AppPreferences();
            }
            catch
            {
                return new AppPreferences();
            }
        }

        public void Save()
        {
            var path = GetSettingsPath();
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(this, options);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }

        private static string GetSettingsPath()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "CherryKeyLayout", "gui-settings.json");
        }
    }
}
