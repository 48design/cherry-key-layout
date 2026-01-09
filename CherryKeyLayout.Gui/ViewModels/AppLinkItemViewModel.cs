using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CherryKeyLayout.Gui.ViewModels
{
    public sealed class AppLinkItemViewModel : INotifyPropertyChanged
    {
        private string _value;

        public AppLinkItemViewModel(string value)
        {
            _value = value;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
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
