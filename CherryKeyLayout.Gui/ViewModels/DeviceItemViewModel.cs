using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CherryKeyLayout.Gui.Services;

namespace CherryKeyLayout.Gui.ViewModels
{
    public sealed class DeviceItemViewModel : INotifyPropertyChanged
    {
        private string _name;
        private string? _imagePath;
        private string? _layoutPath;
        private Dictionary<string, int> _keyMap;

        public DeviceItemViewModel(DeviceConfig config)
        {
            Id = config.Id;
            _name = config.Name;
            _imagePath = config.ImagePath;
            _layoutPath = config.LayoutPath;
            _keyMap = config.KeyMap ?? new Dictionary<string, int>();
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

        public Dictionary<string, int> KeyMap
        {
            get => _keyMap;
            set => SetProperty(ref _keyMap, value);
        }

        public DeviceConfig ToConfig()
        {
            return new DeviceConfig
            {
                Id = Id,
                Name = Name,
                ImagePath = ImagePath,
                LayoutPath = LayoutPath,
                KeyMap = new Dictionary<string, int>(KeyMap)
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
