using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CherryKeyLayout.Gui.Services
{
    public sealed class KeyboardLayout
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public KeyDefinition[] Keys { get; set; } = Array.Empty<KeyDefinition>();

        public static KeyboardLayout Load(string path)
        {
            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var layout = JsonSerializer.Deserialize<KeyboardLayout>(json, options);
            if (layout == null || layout.Keys.Length == 0)
            {
                throw new InvalidOperationException("Key layout file is empty or invalid.");
            }

            return layout;
        }

        public static KeyboardLayout GenerateGrid(int width, int height, int keyCount)
        {
            var columns = 18;
            var rows = (int)Math.Ceiling(keyCount / (double)columns);
            var keyWidth = width / (double)columns;
            var keyHeight = height / (double)rows;

            var keys = new List<KeyDefinition>(keyCount);
            var index = 0;
            for (var col = 0; col < columns; col++)
            {
                for (var row = 0; row < rows; row++)
                {
                    if (index >= keyCount)
                    {
                        break;
                    }

                    keys.Add(new KeyDefinition
                    {
                        Id = $"Key {index + 1}",
                        Index = index,
                        X = col * keyWidth,
                        Y = row * keyHeight,
                        Width = keyWidth,
                        Height = keyHeight
                    });
                    index++;
                }
            }

            return new KeyboardLayout
            {
                Width = width,
                Height = height,
                Keys = keys.ToArray()
            };
        }

        public void Save(string path)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(path, json);
        }
    }

    public sealed class KeyDefinition
    {
        public string? Id { get; set; }
        public int Index { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
