using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using CherryKeyLayout.Gui.Services;

namespace CherryKeyLayout.Gui.ViewModels
{
    /// <summary>
    /// View model for individual keyboard key buttons in the device editor.
    /// 
    /// Features (inspired by Artemis-RGB's LED visualization):
    /// - Position tracking (X, Y) for canvas layout
    /// - Size tracking (Width, Height)
    /// - Color management with visual feedback
    /// - Selection state with visual highlighting
    /// - Semi-transparent rendering for overlay effect
    /// </summary>
    public sealed class KeyButtonViewModel : INotifyPropertyChanged
    {
        private Color _color = Colors.Transparent;
        private IBrush? _fillBrush;
        private double _x;
        private double _y;
        private double _width;
        private double _height;
        private bool _isSelected;

        public KeyButtonViewModel(KeyDefinition definition)
        {
            Id = definition.Id ?? $"Key {definition.Index + 1}";
            Index = definition.Index;
            _x = definition.X;
            _y = definition.Y;
            _width = definition.Width;
            _height = definition.Height;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; }
        public int Index { get; }
        
        public double X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        public double Width
        {
            get => _width;
            set => SetProperty(ref _width, value);
        }

        public double Height
        {
            get => _height;
            set => SetProperty(ref _height, value);
        }

        public Color Color
        {
            get => _color;
            set => SetProperty(ref _color, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FillBrush)));
                }
            }
        }

        public IBrush FillBrush
        {
            get
            {
                // If explicitly set, use that
                if (_fillBrush != null)
                {
                    return _fillBrush;
                }
                
                if (_isSelected)
                {
                    // Highlight selected keys with a brighter overlay
                    var highlightColor = Color.FromArgb(180, 100, 200, 255);
                    return new SolidColorBrush(highlightColor);
                }
                return new SolidColorBrush(Color, 0.6);
            }
            set
            {
                _fillBrush = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FillBrush)));
            }
        }

        public void SetColor(Color color)
        {
            Color = color;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FillBrush)));
        }

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
