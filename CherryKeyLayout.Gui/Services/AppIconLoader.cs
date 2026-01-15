using System;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using DrawingBitmap = System.Drawing.Bitmap;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CherryKeyLayout.Gui.Services
{
    internal static class AppIconLoader
    {
        internal sealed record AppIconData(AvaloniaBitmap Bitmap, string DataUri);

        [SupportedOSPlatform("windows6.1")]
        public static AvaloniaBitmap? TryLoadIcon(string? rawPath)
        {
            var bytes = TryLoadIconPngBytes(rawPath);
            if (bytes == null)
            {
                return null;
            }

            using var stream = new MemoryStream(bytes);
            return new AvaloniaBitmap(stream);
        }

        [SupportedOSPlatform("windows6.1")]
        public static AppIconData? TryLoadIconData(string? rawPath)
        {
            var bytes = TryLoadIconPngBytes(rawPath);
            if (bytes == null)
            {
                return null;
            }

            using var stream = new MemoryStream(bytes);
            var bitmap = new AvaloniaBitmap(stream);
            var dataUri = $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
            return new AppIconData(bitmap, dataUri);
        }

        [SupportedOSPlatform("windows6.1")]
        private static string? ResolveExecutablePath(string? rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return null;
            }

            var candidate = ExtractExecutableCandidate(rawPath);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            candidate = candidate.Trim().Trim('"');
            if (candidate.Contains(Path.DirectorySeparatorChar)
                || candidate.Contains(Path.AltDirectorySeparatorChar)
                || Path.IsPathRooted(candidate))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                try
                {
                    var fullPath = Path.GetFullPath(candidate);
                    return File.Exists(fullPath) ? fullPath : null;
                }
                catch
                {
                    return null;
                }
            }

            var extension = string.IsNullOrWhiteSpace(Path.GetExtension(candidate)) ? ".exe" : null;
            var buffer = new StringBuilder(260);
            var result = SearchPath(null, candidate, extension, buffer.Capacity, buffer, out _);
            if (result > 0)
            {
                return buffer.ToString();
            }

            var registryMatch = TryResolveFromAppPaths(candidate);
            if (!string.IsNullOrWhiteSpace(registryMatch))
            {
                return registryMatch;
            }

            return null;
        }

        [SupportedOSPlatform("windows6.1")]
        private static byte[]? TryLoadIconPngBytes(string? rawPath)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            {
                return null;
            }

            var resolved = ResolveExecutablePath(rawPath);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                return null;
            }

            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(resolved);
                if (icon == null)
                {
                    return null;
                }

                using DrawingBitmap bitmap = icon.ToBitmap();
                using var stream = new MemoryStream();
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                return stream.ToArray();
            }
            catch
            {
                return null;
            }
        }

        public static string GetExecutableName(string raw)
        {
            var candidate = ExtractExecutableCandidate(raw);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return string.Empty;
            }

            return Path.GetFileName(candidate.Trim().Trim('"'));
        }

        private static string ExtractExecutableCandidate(string raw)
        {
            var trimmed = raw.Trim();
            if (trimmed.StartsWith("\"", StringComparison.Ordinal))
            {
                var end = trimmed.IndexOf('"', 1);
                if (end > 1)
                {
                    return trimmed.Substring(1, end - 1);
                }
            }

            var spaceIndex = trimmed.IndexOf(' ');
            return spaceIndex > 0 ? trimmed.Substring(0, spaceIndex) : trimmed;
        }

        [SupportedOSPlatform("windows")]
        private static string? TryResolveFromAppPaths(string executableName)
        {
            var name = executableName;
            if (string.IsNullOrWhiteSpace(Path.GetExtension(name)))
            {
                name += ".exe";
            }

            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using var key = root.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{name}");
                    var path = key?.GetValue(string.Empty) as string;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        return path;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int SearchPath(
            string? path,
            string fileName,
            string? extension,
            int bufferLength,
            StringBuilder buffer,
            out IntPtr filePart);
    }
}
