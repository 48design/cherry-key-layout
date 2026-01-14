using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using Svg.Skia;

namespace CherryKeyLayout.Gui.Converters
{
    public sealed class SvgAssetToBitmapConverter : IValueConverter
    {
        public static readonly SvgAssetToBitmapConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string assetUri)
            {
                return null;
            }

            var size = ParseSize(parameter?.ToString());
            return RenderSvg(assetUri, size.Width, size.Height);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }

        private static Bitmap? RenderSvg(string assetUri, int width, int height)
        {
            try
            {
                using var stream = OpenSvgStream(assetUri);
                var svg = new SKSvg();
                var picture = svg.Load(stream);
                if (picture == null)
                {
                    return null;
                }

                var rect = picture.CullRect;
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    return null;
                }

                var scale = Math.Min((float)width / rect.Width, (float)height / rect.Height);
                if (scale <= 0)
                {
                    return null;
                }

                var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(info);
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(scale);

                var dx = ((float)width / scale - rect.Width) / 2f - rect.Left;
                var dy = ((float)height / scale - rect.Height) / 2f - rect.Top;
                canvas.Translate(dx, dy);
                canvas.DrawPicture(picture);
                canvas.Flush();

                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var bitmapStream = new MemoryStream();
                data.SaveTo(bitmapStream);
                bitmapStream.Position = 0;
                return new Bitmap(bitmapStream);
            }
            catch
            {
                return null;
            }
        }

        private static Stream OpenSvgStream(string assetUri)
        {
            var uri = new Uri(assetUri);
            if (AssetLoader.Exists(uri))
            {
                return AssetLoader.Open(uri);
            }

            var fileName = System.IO.Path.GetFileName(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var fallbackPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
                if (File.Exists(fallbackPath))
                {
                    return File.OpenRead(fallbackPath);
                }
            }

            throw new FileNotFoundException("SVG asset not found.", assetUri);
        }

        private static (int Width, int Height) ParseSize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return (16, 16);
            }

            var trimmed = raw.Trim();
            var separators = new[] { 'x', 'X', ',', ';', ' ' };
            var parts = trimmed.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
            {
                return (Math.Max(1, size), Math.Max(1, size));
            }

            if (parts.Length >= 2
                && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
            {
                return (Math.Max(1, width), Math.Max(1, height));
            }

            return (16, 16);
        }
    }
}
