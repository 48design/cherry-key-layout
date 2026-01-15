using System.ComponentModel;
using System.Runtime.CompilerServices;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace CherryKeyLayout.Gui.ViewModels
{
    public sealed class ProfileIconOptionViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public ProfileIconOptionViewModel(AvaloniaBitmap? icon, string? dataUri, string? sourceName, string? toolTip, bool isNone = false)
        {
            Icon = icon;
            DataUri = dataUri;
            SourceName = sourceName;
            ToolTip = toolTip;
            IsNone = isNone;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public AvaloniaBitmap? Icon { get; }
        public string? DataUri { get; }
        public string? SourceName { get; }
        public string? ToolTip { get; }
        public bool IsNone { get; }
        public bool HasIcon => Icon != null;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
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
