using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CherryKeyLayout.Gui
{
    public sealed partial class ExitDialogWindow : Window
    {
        public enum ExitDialogResult
        {
            Cancel,
            Minimize,
            Exit
        }

        public ExitDialogWindow()
        {
            InitializeComponent();
        }

        private void OnMinimizeClicked(object? sender, RoutedEventArgs e)
        {
            Close(ExitDialogResult.Minimize);
        }

        private void OnExitClicked(object? sender, RoutedEventArgs e)
        {
            Close(ExitDialogResult.Exit);
        }

        private void OnCancelClicked(object? sender, RoutedEventArgs e)
        {
            Close(ExitDialogResult.Cancel);
        }
    }
}
