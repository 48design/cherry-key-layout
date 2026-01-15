using System;
using System.IO;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace CherryKeyLayout.Gui.Services
{
    internal static class ProfileImageHelper
    {
        public static AvaloniaBitmap? TryDecodeDataUri(string? dataUri)
        {
            if (string.IsNullOrWhiteSpace(dataUri))
            {
                return null;
            }

            var marker = "base64,";
            var index = dataUri.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return null;
            }

            var payload = dataUri[(index + marker.Length)..].Trim();
            if (payload.Length == 0)
            {
                return null;
            }

            try
            {
                var bytes = Convert.FromBase64String(payload);
                using var stream = new MemoryStream(bytes);
                return new AvaloniaBitmap(stream);
            }
            catch
            {
                return null;
            }
        }
    }
}
