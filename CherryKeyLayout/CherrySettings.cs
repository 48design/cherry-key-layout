using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CherryKeyLayout
{
    internal sealed class CherrySettingsLighting
    {
        public LightingMode Mode { get; set; } = LightingMode.Static;
        public Brightness Brightness { get; set; } = Brightness.Full;
        public Speed Speed { get; set; } = Speed.Medium;
        public Rgb Color { get; set; } = new Rgb(255, 0, 0);
        public int Delay { get; set; } = 2;
        public string? Direction { get; set; }
        public string? Random { get; set; }
        public Rgb[]? CustomColors { get; set; }
    }

    internal static class CherrySettings
    {
        public static (int SelectedIndex, string?[] Titles) ListProfiles(string path)
        {
            var root = LoadRoot(path);
            var profilesRoot = FindFirstObjectWithProperty(root, "profilesList");
            var profilesList = profilesRoot?["profilesList"]?["content"] as JsonArray;
            if (profilesList == null)
            {
                throw new InvalidOperationException("No profiles found in settings file.");
            }

            var selectedIndex = profilesRoot?["selectedProfile"]?.GetValue<int>() ?? 0;
            var titles = profilesList
                .Select(p => p?["content"]?["info"]?["content"]?["title"]?["content"]?.GetValue<string>())
                .ToArray();

            return (selectedIndex, titles);
        }

        public static CherrySettingsLighting LoadLighting(string path, int? profileIndex = null)
        {
            var root = LoadRoot(path);
            var lightings = GetLightingNode(root, profileIndex);

            var brightness = lightings["brightness"]?.GetValue<int>() ?? (int)Brightness.Full;
            var modeText = lightings["mode"]?.GetValue<string>() ?? "Static";
            var delay = lightings["delay"]?.GetValue<int>() ?? 2;

            var colorNode = lightings["color"]?["content"] as JsonObject;
            var r = colorNode?["r"]?.GetValue<int>() ?? 0;
            var g = colorNode?["g"]?.GetValue<int>() ?? 0;
            var b = colorNode?["b"]?.GetValue<int>() ?? 0;

            var customColors = ParseCustomColors(lightings);

            return new CherrySettingsLighting
            {
                Brightness = (Brightness)Math.Clamp(brightness, 0, 4),
                Mode = ParseMode(modeText),
                Delay = delay,
                Speed = SpeedFromDelay(delay),
                Color = new Rgb((byte)r, (byte)g, (byte)b),
                Direction = lightings["direction"]?.GetValue<string>(),
                Random = lightings["random"]?.GetValue<string>(),
                CustomColors = customColors
            };
        }

        public static void SaveLighting(string path, CherrySettingsLighting lighting, int? profileIndex = null)
        {
            var root = LoadRoot(path);
            var lightings = GetLightingNode(root, profileIndex);

            lightings["brightness"] = (int)lighting.Brightness;
            lightings["mode"] = ModeToString(lighting.Mode);
            lightings["delay"] = DelayFromSpeed(lighting.Speed);

            if (lighting.Direction != null)
            {
                lightings["direction"] = lighting.Direction;
            }

            if (lighting.Random != null)
            {
                lightings["random"] = lighting.Random;
            }

            var colorNode = EnsureColorContent(lightings);
            colorNode["a"] = 255;
            colorNode["spec"] = 1;
            colorNode["r"] = lighting.Color.R;
            colorNode["g"] = lighting.Color.G;
            colorNode["b"] = lighting.Color.B;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, root.ToJsonString(options));
        }

        public static void SetSelectedProfile(string path, int profileIndex)
        {
            var root = LoadRoot(path);
            var profilesRoot = FindFirstObjectWithProperty(root, "profilesList");
            var profilesList = profilesRoot?["profilesList"]?["content"] as JsonArray;
            if (profilesRoot == null || profilesList == null)
            {
                throw new InvalidOperationException("No profiles found in settings file.");
            }

            var selectedIndex = NormalizeProfileIndex(profileIndex, profilesList.Count);
            profilesRoot["selectedProfile"] = selectedIndex;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, root.ToJsonString(options));
        }

        private static JsonObject LoadRoot(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Settings file not found.", path);
            }

            var node = JsonNode.Parse(File.ReadAllText(path));
            if (node is not JsonObject root)
            {
                throw new InvalidOperationException("Settings file is not a JSON object.");
            }

            return root;
        }

        private static JsonObject GetLightingNode(JsonObject root, int? profileIndex)
        {
            var profilesRoot = FindFirstObjectWithProperty(root, "profilesList");
            var profilesList = profilesRoot?["profilesList"]?["content"] as JsonArray;
            if (profilesList == null || profilesList.Count == 0)
            {
                throw new InvalidOperationException("No profiles found in settings file.");
            }

            var selectedIndex = profilesRoot?["selectedProfile"]?.GetValue<int>() ?? 0;
            selectedIndex = NormalizeProfileIndex(profileIndex ?? selectedIndex, profilesList.Count);

            var profile = profilesList[selectedIndex]?["content"];
            var devicesContainer = FindFirstObjectWithProperty(profile, "devices");
            var devices = devicesContainer?["devices"] as JsonObject;
            if (devices == null || devices.Count == 0)
            {
                throw new InvalidOperationException("No devices found in selected profile.");
            }

            foreach (var device in devices)
            {
                var lightings = GetLightingsFromDevice(device.Value);
                if (lightings != null)
                {
                    return lightings;
                }
            }

            throw new InvalidOperationException("No lighting data found in settings file.");
        }

        private static JsonObject? GetLightingsFromDevice(JsonNode? deviceNode)
        {
            var lightings = deviceNode?["Lightings"]?["content"]?["content"] as JsonObject;
            if (lightings != null)
            {
                return lightings;
            }

            return deviceNode?["lightings"]?["content"]?["content"] as JsonObject;
        }

        private static JsonObject? FindFirstObjectWithProperty(JsonNode? node, string propertyName)
        {
            if (node == null)
            {
                return null;
            }

            if (node is JsonObject obj)
            {
                if (obj.ContainsKey(propertyName))
                {
                    return obj;
                }

                foreach (var kvp in obj)
                {
                    var found = FindFirstObjectWithProperty(kvp.Value, propertyName);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var child in array)
                {
                    var found = FindFirstObjectWithProperty(child, propertyName);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }

        private static int NormalizeProfileIndex(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            if (index < 0 || index >= count)
            {
                return 0;
            }

            return index;
        }

        private static JsonObject EnsureColorContent(JsonObject lightings)
        {
            if (lightings["color"] is not JsonObject colorNode)
            {
                colorNode = new JsonObject();
                lightings["color"] = colorNode;
            }

            if (colorNode["content"] is not JsonObject content)
            {
                content = new JsonObject();
                colorNode["content"] = content;
            }

            if (colorNode["version"] == null)
            {
                colorNode["version"] = 0;
            }

            return content;
        }

        private static Rgb[]? ParseCustomColors(JsonObject lightings)
        {
            var list = lightings["customColors"]?["content"] as JsonArray;
            if (list == null || list.Count == 0)
            {
                return null;
            }

            var colors = list
                .Select(item =>
                {
                    var content = item?["content"] as JsonObject;
                    if (content == null)
                    {
                        return new Rgb(0, 0, 0);
                    }

                    var r = content["r"]?.GetValue<int>() ?? 0;
                    var g = content["g"]?.GetValue<int>() ?? 0;
                    var b = content["b"]?.GetValue<int>() ?? 0;
                    return new Rgb((byte)r, (byte)g, (byte)b);
                })
                .ToArray();

            return colors;
        }

        private static LightingMode ParseMode(string value)
        {
            if (Enum.TryParse(value, ignoreCase: true, out LightingMode mode))
            {
                return mode;
            }

            return LightingMode.Static;
        }

        private static string ModeToString(LightingMode mode)
        {
            return mode.ToString();
        }

        private static Speed SpeedFromDelay(int delay)
        {
            return delay switch
            {
                <= 0 => Speed.VeryFast,
                1 => Speed.Fast,
                2 => Speed.Medium,
                3 => Speed.Slow,
                _ => Speed.VerySlow
            };
        }

        private static int DelayFromSpeed(Speed speed)
        {
            return speed switch
            {
                Speed.VeryFast => 0,
                Speed.Fast => 1,
                Speed.Medium => 2,
                Speed.Slow => 3,
                _ => 4
            };
        }
    }
}
