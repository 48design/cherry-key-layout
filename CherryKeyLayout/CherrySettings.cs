using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CherryKeyLayout
{
    public sealed class CherrySettingsLighting
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

    public sealed class CherryProfileInfo
    {
        public int Index { get; init; }
        public string? Title { get; init; }
        public bool AppEnabled { get; init; }
        public string[] AppPaths { get; init; } = Array.Empty<string>();
    }

    public static class CherrySettings
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

        public static (int SelectedIndex, CherryProfileInfo[] Profiles) LoadProfiles(string path)
        {
            var root = LoadRoot(path);
            var profilesRoot = FindFirstObjectWithProperty(root, "profilesList");
            var profilesList = profilesRoot?["profilesList"]?["content"] as JsonArray;
            if (profilesList == null)
            {
                throw new InvalidOperationException("No profiles found in settings file.");
            }

            var selectedIndex = profilesRoot?["selectedProfile"]?.GetValue<int>() ?? 0;
            var profiles = profilesList
                .Select((profile, index) =>
                {
                    var info = profile?["content"]?["info"]?["content"] as JsonObject;
                    var title = info?["title"]?["content"]?.GetValue<string>();
                    var appEnabled = info?["appEnabled"]?.GetValue<bool>() ?? false;
                    var appList = info?["appList"]?["content"] as JsonArray;
                    var appPaths = appList == null
                        ? Array.Empty<string>()
                        : appList
                            .Select(app => app?["content"]?.GetValue<string>())
                            .Where(pathValue => !string.IsNullOrWhiteSpace(pathValue))
                            .Select(pathValue => NormalizeAppPath(pathValue!))
                            .Where(pathValue => !string.IsNullOrWhiteSpace(pathValue))
                            .ToArray();

                    return new CherryProfileInfo
                    {
                        Index = index,
                        Title = title,
                        AppEnabled = appEnabled,
                        AppPaths = appPaths
                    };
                })
                .ToArray();

            return (selectedIndex, profiles);
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

            if (lighting.CustomColors != null)
            {
                SaveCustomColorsToNode(lightings, lighting.CustomColors);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, root.ToJsonString(options));
        }

        public static void SaveCustomColors(string path, Rgb[] colors, int? profileIndex = null)
        {
            var root = LoadRoot(path);
            var lightings = GetLightingNode(root, profileIndex);

            lightings["mode"] = ModeToString(LightingMode.Custom);
            SaveCustomColorsToNode(lightings, colors);

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

        public static void SetProfileTitle(string path, int profileIndex, string title)
        {
            var root = LoadRoot(path);
            var profileInfo = GetProfileInfoNode(root, profileIndex);
            var titleNode = profileInfo["title"] as JsonObject ?? new JsonObject();
            titleNode["content"] = title;
            if (titleNode["version"] == null)
            {
                titleNode["version"] = 0;
            }

            profileInfo["title"] = titleNode;
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, root.ToJsonString(options));
        }

        public static void SetProfileApps(string path, int profileIndex, string[] apps)
        {
            var root = LoadRoot(path);
            var profileInfo = GetProfileInfoNode(root, profileIndex);
            var appList = new JsonArray();
            foreach (var app in apps ?? Array.Empty<string>())
            {
                var trimmed = app?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                var entry = new JsonObject
                {
                    ["content"] = trimmed,
                    ["version"] = 0
                };
                appList.Add(entry);
            }

            var appListNode = profileInfo["appList"] as JsonObject ?? new JsonObject();
            appListNode["content"] = appList;
            if (appListNode["version"] == null)
            {
                appListNode["version"] = 0;
            }

            profileInfo["appList"] = appListNode;
            profileInfo["appEnabled"] = appList.Count > 0;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, root.ToJsonString(options));
        }

        public static int AddProfile(string path, int? sourceProfileIndex = null)
        {
            var root = LoadRoot(path);
            var profilesRoot = FindFirstObjectWithProperty(root, "profilesList");
            var profilesList = profilesRoot?["profilesList"]?["content"] as JsonArray;
            if (profilesRoot == null || profilesList == null || profilesList.Count == 0)
            {
                throw new InvalidOperationException("No profiles found in settings file.");
            }

            var selectedIndex = profilesRoot["selectedProfile"]?.GetValue<int>() ?? 0;
            var sourceIndex = NormalizeProfileIndex(sourceProfileIndex ?? selectedIndex, profilesList.Count);
            var sourceProfile = profilesList[sourceIndex];
            var clone = sourceProfile?.DeepClone();
            if (clone is not JsonObject cloneObject)
            {
                throw new InvalidOperationException("Failed to clone profile.");
            }

            var newIndex = profilesList.Count;
            var infoNode = cloneObject["content"]?["info"]?["content"] as JsonObject;
            if (infoNode != null)
            {
                var titleNode = infoNode["title"] as JsonObject ?? new JsonObject();
                titleNode["content"] = $"Profile {newIndex + 1}";
                if (titleNode["version"] == null)
                {
                    titleNode["version"] = 0;
                }
                infoNode["title"] = titleNode;
            }

            profilesList.Add(cloneObject);
            profilesRoot["selectedProfile"] = newIndex;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, root.ToJsonString(options));

            return newIndex;
        }

        public static void RemoveProfile(string path, int profileIndex)
        {
            var root = LoadRoot(path);
            var profilesRoot = FindFirstObjectWithProperty(root, "profilesList");
            var profilesList = profilesRoot?["profilesList"]?["content"] as JsonArray;
            if (profilesRoot == null || profilesList == null || profilesList.Count == 0)
            {
                throw new InvalidOperationException("No profiles found in settings file.");
            }

            if (profilesList.Count <= 1)
            {
                throw new InvalidOperationException("At least one profile must remain.");
            }

            var index = NormalizeProfileIndex(profileIndex, profilesList.Count);
            profilesList.RemoveAt(index);

            var selectedIndex = profilesRoot["selectedProfile"]?.GetValue<int>() ?? 0;
            if (selectedIndex == index)
            {
                selectedIndex = Math.Clamp(index - 1, 0, profilesList.Count - 1);
            }
            else if (selectedIndex > index)
            {
                selectedIndex--;
            }

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

        private static JsonObject GetProfileInfoNode(JsonObject root, int profileIndex)
        {
            var profilesRoot = FindFirstObjectWithProperty(root, "profilesList");
            var profilesList = profilesRoot?["profilesList"]?["content"] as JsonArray;
            if (profilesList == null || profilesList.Count == 0)
            {
                throw new InvalidOperationException("No profiles found in settings file.");
            }

            var selectedIndex = NormalizeProfileIndex(profileIndex, profilesList.Count);
            var profile = profilesList[selectedIndex]?["content"] as JsonObject;
            var info = profile?["info"]?["content"] as JsonObject;
            if (info == null)
            {
                throw new InvalidOperationException("No profile info found in settings file.");
            }

            return info;
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

        private static string NormalizeAppPath(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                return trimmed;
            }

            if (!trimmed.Contains('/') && !trimmed.Contains('\\') && !Path.IsPathRooted(trimmed))
            {
                return Path.GetFileName(trimmed);
            }

            return trimmed.Replace('\\', '/');
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

        private static void SaveCustomColorsToNode(JsonObject lightings, Rgb[] colors)
        {
            var customColorsNode = lightings["customColors"] as JsonObject ?? new JsonObject();
            var list = new JsonArray();
            var total = CherryProtocol.TotalKeys;

            for (var i = 0; i < total; i++)
            {
                var color = i < colors.Length ? colors[i] : new Rgb(0, 0, 0);
                var content = new JsonObject
                {
                    ["a"] = 255,
                    ["r"] = color.R,
                    ["g"] = color.G,
                    ["b"] = color.B,
                    ["spec"] = 1
                };
                var entry = new JsonObject
                {
                    ["content"] = content,
                    ["version"] = 0
                };
                list.Add(entry);
            }

            customColorsNode["content"] = list;
            if (customColorsNode["version"] == null)
            {
                customColorsNode["version"] = 0;
            }

            lightings["customColors"] = customColorsNode;
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
