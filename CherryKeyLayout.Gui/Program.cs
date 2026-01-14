using System;
using System.IO;
using Avalonia;

namespace CherryKeyLayout.Gui
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                try
                {
                    var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    var logDir = Path.Combine(basePath, "CherryKeyLayout");
                    Directory.CreateDirectory(logDir);
                    var logPath = Path.Combine(logDir, "crash.log");
                    File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Program.Main\n{ex}\n");
                }
                catch
                {
                }

                Console.Error.WriteLine(ex);
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
