using System;

namespace CherryKeyLayout.Gui.ViewModels
{
    public sealed class RunningAppItemViewModel
    {
        private const int MaxTitleLength = 40;
        private const int MaxExeLength = 32;

        public RunningAppItemViewModel(string title, string path)
        {
            Title = title;
            Path = path;
            ExeName = System.IO.Path.GetFileName(path);
        }

        public string Title { get; }
        public string Path { get; }
        public string ExeName { get; }
        public string TitleDisplay => TruncateMiddle(Title, MaxTitleLength);
        public string ExeDisplay => TruncateMiddle(ExeName, MaxExeLength);
        public string DisplayLabel => string.IsNullOrWhiteSpace(Title) ? ExeDisplay : $"{TitleDisplay} ({ExeDisplay})";

        private static string TruncateMiddle(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || maxLength <= 0)
            {
                return value;
            }

            if (value.Length <= maxLength)
            {
                return value;
            }

            if (maxLength <= 4)
            {
                return value.Substring(0, maxLength);
            }

            var keepStart = (maxLength - 3) / 2;
            var keepEnd = maxLength - 3 - keepStart;
            return value.Substring(0, keepStart) + "..." + value.Substring(value.Length - keepEnd);
        }
    }
}
