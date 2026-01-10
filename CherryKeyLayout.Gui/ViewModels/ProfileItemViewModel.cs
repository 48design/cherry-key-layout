using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CherryKeyLayout.Gui.ViewModels
{
    public sealed class ProfileItemViewModel : INotifyPropertyChanged
    {
        private bool _isDefault;
        private string _title;

        public ProfileItemViewModel(int index, string? title, bool appEnabled, IReadOnlyList<string> appPaths, bool isDefault)
        {
            Index = index;
            _title = string.IsNullOrWhiteSpace(title) ? $"Profile {index + 1}" : title!;
            AppEnabled = appEnabled;
            AppPaths = appPaths?.ToArray() ?? Array.Empty<string>();
            _isDefault = isDefault;
            AppSummary = appEnabled ? $"{AppPaths.Length} linked app(s)" : string.Empty;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Index { get; }
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }
        public bool AppEnabled { get; }
        public string[] AppPaths { get; }
        public string AppSummary { get; }

        public bool IsDefault
        {
            get => _isDefault;
            set => SetProperty(ref _isDefault, value);
        }

        public string DefaultLabel => IsDefault ? "Default" : string.Empty;

        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            if (propertyName == nameof(IsDefault))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DefaultLabel)));
            }
        }
    }
}
