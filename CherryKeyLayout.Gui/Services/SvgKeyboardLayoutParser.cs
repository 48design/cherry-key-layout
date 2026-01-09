using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CherryKeyLayout.Gui.ViewModels;

namespace CherryKeyLayout.Gui.Services
{
    public static class SvgKeyboardLayoutParser
    {
        public static List<KeyDefinition> Parse(string svgPath)
        {
            var keys = new List<KeyDefinition>();
            var doc = XDocument.Load(svgPath);
            var ns = doc.Root?.Name.Namespace;
            if (doc.Root == null || ns == null) return keys;
            foreach (var path in doc.Descendants(ns + "path"))
            {
                var d = path.Attribute("d")?.Value;
                if (d == null) continue;
                // Match rectangle: d="mX Y w h ..."
                var match = Regex.Match(d, @"m([\d\.]+) ([\d\.]+)h([\d\.]+).+v([\d\.]+)");
                if (!match.Success) continue;
                double x = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                double y = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                double w = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                double h = double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
                var id = path.Attribute("id")?.Value ?? $"key_{keys.Count + 1}";
                keys.Add(new KeyDefinition
                {
                    Id = id,
                    Index = keys.Count,
                    X = x,
                    Y = y,
                    Width = w,
                    Height = h
                });
            }
            return keys;
        }
    }
}
