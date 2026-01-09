using System.ComponentModel;
using System.Runtime.CompilerServices;
using CherryKeyLayout.Gui.Services;

namespace CherryKeyLayout.Gui.ViewModels
{
    public sealed class DeviceItemViewModel : INotifyPropertyChanged
    {
        private string _name;
        private string? _imagePath;
        private string? _layoutPath;

        public DeviceItemViewModel(DeviceConfig config)
        {
            Id = config.Id;
            _name = config.Name;
            _imagePath = config.ImagePath;
            _layoutPath = config.LayoutPath;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string? ImagePath
        {
            get => _imagePath;
            set => SetProperty(ref _imagePath, value);
        }

        public string? LayoutPath
        {
            get => _layoutPath;
            set => SetProperty(ref _layoutPath, value);
        }

        public DeviceConfig ToConfig()
        {
            return new DeviceConfig
            {
                Id = Id,
                Name = Name,
                ImagePath = ImagePath,
                LayoutPath = LayoutPath
            };
        }

        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
