using System.ComponentModel;
using System.Runtime.CompilerServices;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using CherryKeyLayout.Gui.Services;

namespace CherryKeyLayout.Gui.ViewModels
{
    public sealed class AppLinkItemViewModel : INotifyPropertyChanged
    {
        private string _value;
        private AvaloniaBitmap? _icon;
        private string? _iconDataUri;

        public AppLinkItemViewModel(string value, string? iconDataUri = null)
        {
            _value = value;
            _iconDataUri = iconDataUri;
            _icon = ProfileImageHelper.TryDecodeDataUri(iconDataUri);

            if (_icon == null && OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            {
                var iconData = AppIconLoader.TryLoadIconData(_value);
                if (iconData != null)
                {
                    _icon = iconData.Bitmap;
                    _iconDataUri = iconData.DataUri;
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        public AvaloniaBitmap? Icon
        {
            get => _icon;
            private set => SetProperty(ref _icon, value);
        }

        public bool HasIcon => _icon != null;

        public string? IconDataUri
        {
            get => _iconDataUri;
            private set => SetProperty(ref _iconDataUri, value);
        }

        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            if (propertyName == nameof(Value))
            {
                if (string.IsNullOrWhiteSpace(_value))
                {
                    Icon = null;
                    IconDataUri = null;
                }
                else if (OperatingSystem.IsWindowsVersionAtLeast(6, 1))
                {
                    var nextIcon = AppIconLoader.TryLoadIconData(_value);
                    if (nextIcon != null)
                    {
                        Icon = nextIcon.Bitmap;
                        IconDataUri = nextIcon.DataUri;
                    }
                }
            }
            else if (propertyName == nameof(Icon))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIcon)));
            }
            else if (propertyName == nameof(IconDataUri))
            {
                return;
            }
        }
    }
}
