using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CherryKeyLayout
{
    internal static class Program
    {
        private const ushort CherryVid = 0x046A;

        private static int Main(string[] args)
        {
            try
            {
                var options = AppOptions.Parse(args);
                if (options.ShowHelp)
                {
                    PrintUsage();
                    return 0;
                }

                if (!string.IsNullOrWhiteSpace(options.ListProfilesPath))
                {
                    var (selectedIndex, titles) = CherrySettings.ListProfiles(options.ListProfilesPath);
                    Console.WriteLine("Profiles:");
                    for (var i = 0; i < titles.Length; i++)
                    {
                        var title = string.IsNullOrWhiteSpace(titles[i]) ? "(untitled)" : titles[i];
                        var marker = i == selectedIndex ? "*" : " ";
                        Console.WriteLine($"{marker} [{i}] {title}");
                    }

                    return 0;
                }

                string? profileSummary = null;
                CherrySettingsLighting? loadedLighting = null;
                if (!string.IsNullOrWhiteSpace(options.LoadSettingsPath))
                {
                    var (selectedIndex, titles) = CherrySettings.ListProfiles(options.LoadSettingsPath);
                    var effectiveIndex = options.ProfileIndex ?? selectedIndex;
                    var formattedTitles = titles
                        .Select((title, index) =>
                        {
                            var name = string.IsNullOrWhiteSpace(title) ? "(untitled)" : title;
                            return $"{index}: {name}";
                        })
                        .ToArray();
                    var selectedTitle = effectiveIndex >= 0 && effectiveIndex < titles.Length
                        ? (string.IsNullOrWhiteSpace(titles[effectiveIndex]) ? "(untitled)" : titles[effectiveIndex])
                        : "(unknown)";
                    profileSummary = $"Selected profile: {effectiveIndex}: {selectedTitle}. Profiles: {string.Join(", ", formattedTitles)}";

                    loadedLighting = CherrySettings.LoadLighting(options.LoadSettingsPath, options.ProfileIndex);
                    options.ApplyLighting(loadedLighting);
                }

                if (options.SelectProfileIndex.HasValue)
                {
                    var selectPath = options.LoadSettingsPath ?? options.SaveSettingsPath;
                    if (string.IsNullOrWhiteSpace(selectPath))
                    {
                        throw new ArgumentException("Select profile requires --load-settings or --save-settings.");
                    }

                    CherrySettings.SetSelectedProfile(selectPath, options.SelectProfileIndex.Value);
                }

                using var keyboard = CherryKeyboard.Open(CherryVid, options.ProductId);

                var useCustom = loadedLighting?.Mode == LightingMode.Custom
                    && loadedLighting.CustomColors != null
                    && loadedLighting.CustomColors.Length > 0;

                if (useCustom)
                {
                    keyboard.SetCustomColors(loadedLighting!.CustomColors!, options.Brightness, options.Speed);
                }
                else if (options.Mode == LightingMode.Static)
                {
                    keyboard.SetStaticColor(options.Color, options.Brightness);
                }
                else
                {
                    keyboard.SetAnimation(options.Mode, options.Brightness, options.Speed, options.Color, options.Rainbow);
                }

                if (!string.IsNullOrWhiteSpace(options.SaveSettingsPath))
                {
                    CherrySettings.SaveLighting(options.SaveSettingsPath, options.ToLighting(), options.ProfileIndex);
                }

                if (string.IsNullOrWhiteSpace(profileSummary))
                {
                    Console.WriteLine("Done.");
                }
                else
                {
                    Console.WriteLine($"Done. {profileSummary}");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("CherryKeyLayout - CHERRY MX Board 3.0S RGB HID controller");
            Console.WriteLine("Usage:");
            Console.WriteLine("  CherryKeyLayout [--pid 0x00DD] [--mode static|wave] [--color #RRGGBB] [--brightness off|low|medium|high|full] [--speed veryfast|fast|medium|slow|veryslow] [--rainbow] [--load-settings path] [--save-settings path] [--list-profiles path] [--profile-index n] [--select-profile n]");
            Console.WriteLine();
            Console.WriteLine("Defaults:");
            Console.WriteLine("  mode=static, color=#FF0000, brightness=full, speed=medium");
        }
    }

    internal sealed class AppOptions
    {
        public ushort? ProductId { get; private set; }
        public LightingMode Mode { get; private set; } = LightingMode.Static;
        public Speed Speed { get; private set; } = Speed.Medium;
        public Brightness Brightness { get; private set; } = Brightness.Full;
        public Rgb Color { get; private set; } = new Rgb(0xFF, 0x00, 0x00);
        public bool Rainbow { get; private set; }
        public bool ShowHelp { get; private set; }
        public string? LoadSettingsPath { get; private set; }
        public string? SaveSettingsPath { get; private set; }
        public string? ListProfilesPath { get; private set; }
        public int? ProfileIndex { get; private set; }
        public int? SelectProfileIndex { get; private set; }

        public static AppOptions Parse(string[] args)
        {
            var options = new AppOptions();
            var queue = new Queue<string>(args);

            while (queue.Count > 0)
            {
                var arg = queue.Dequeue();
                switch (arg)
                {
                    case "--help":
                    case "-h":
                    case "/?":
                        options.ShowHelp = true;
                        return options;
                    case "--pid":
                        options.ProductId = ParseUShort(queue, "--pid");
                        break;
                    case "--mode":
                        options.Mode = ParseEnum<LightingMode>(queue, "--mode");
                        break;
                    case "--speed":
                        options.Speed = ParseEnum<Speed>(queue, "--speed");
                        break;
                    case "--brightness":
                        options.Brightness = ParseEnum<Brightness>(queue, "--brightness");
                        break;
                    case "--color":
                        options.Color = ParseColor(queue, "--color");
                        break;
                    case "--rainbow":
                        options.Rainbow = true;
                        break;
                    case "--load-settings":
                        options.LoadSettingsPath = ParseString(queue, "--load-settings");
                        break;
                    case "--save-settings":
                        options.SaveSettingsPath = ParseString(queue, "--save-settings");
                        break;
                    case "--list-profiles":
                        options.ListProfilesPath = ParseString(queue, "--list-profiles");
                        break;
                    case "--profile-index":
                        options.ProfileIndex = ParseInt(queue, "--profile-index");
                        break;
                    case "--select-profile":
                        options.SelectProfileIndex = ParseInt(queue, "--select-profile");
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument: {arg}");
                }
            }

            return options;
        }

        private static ushort ParseUShort(Queue<string> queue, string name)
        {
            if (queue.Count == 0)
            {
                throw new ArgumentException($"Missing value for {name}.");
            }

            var raw = queue.Dequeue();
            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return ushort.Parse(raw.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return ushort.Parse(raw, CultureInfo.InvariantCulture);
        }

        private static T ParseEnum<T>(Queue<string> queue, string name) where T : struct
        {
            if (queue.Count == 0)
            {
                throw new ArgumentException($"Missing value for {name}.");
            }

            var raw = queue.Dequeue();
            if (Enum.TryParse(raw, ignoreCase: true, out T value))
            {
                return value;
            }

            throw new ArgumentException($"Invalid {name} value: {raw}.");
        }

        private static string ParseString(Queue<string> queue, string name)
        {
            if (queue.Count == 0)
            {
                throw new ArgumentException($"Missing value for {name}.");
            }

            return queue.Dequeue();
        }

        private static int ParseInt(Queue<string> queue, string name)
        {
            if (queue.Count == 0)
            {
                throw new ArgumentException($"Missing value for {name}.");
            }

            var raw = queue.Dequeue();
            return int.Parse(raw, CultureInfo.InvariantCulture);
        }

        private static Rgb ParseColor(Queue<string> queue, string name)
        {
            if (queue.Count == 0)
            {
                throw new ArgumentException($"Missing value for {name}.");
            }

            var raw = queue.Dequeue();
            if (raw.StartsWith("#", StringComparison.Ordinal))
            {
                raw = raw.Substring(1);
            }

            if (raw.Length == 6 && int.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                var r = (byte)((rgb >> 16) & 0xFF);
                var g = (byte)((rgb >> 8) & 0xFF);
                var b = (byte)(rgb & 0xFF);
                return new Rgb(r, g, b);
            }

            var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3)
            {
                return new Rgb(
                    byte.Parse(parts[0], CultureInfo.InvariantCulture),
                    byte.Parse(parts[1], CultureInfo.InvariantCulture),
                    byte.Parse(parts[2], CultureInfo.InvariantCulture));
            }

            throw new ArgumentException($"Invalid {name} value: {raw}.");
        }

        public void ApplyLighting(CherrySettingsLighting lighting)
        {
            Mode = lighting.Mode;
            Brightness = lighting.Brightness;
            Speed = lighting.Speed;
            Color = lighting.Color;
        }

        public CherrySettingsLighting ToLighting()
        {
            return new CherrySettingsLighting
            {
                Mode = Mode,
                Brightness = Brightness,
                Speed = Speed,
                Color = Color
            };
        }
    }
}
