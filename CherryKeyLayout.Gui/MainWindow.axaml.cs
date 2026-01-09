using System;
using System.IO;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Controls.Shapes;
using CherryKeyLayout.Gui.ViewModels;

namespace CherryKeyLayout.Gui
{
    public sealed partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private KeyButtonViewModel? _activeKey;
        private string? _activeResizeHandle;
        private double _dragOffsetX;
        private double _dragOffsetY;
        private bool _isDragging;
        private Viewbox? _keyboardViewbox;
        private const double ResizeStep = 2.0;
        private const double MinKeySize = 6.0;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainWindowViewModel();
            DataContext = _viewModel;
            Closed += (_, __) => _viewModel.Dispose();
            
            _keyboardViewbox = this.FindControl<Viewbox>("KeyboardViewbox");
            AddHandler(InputElement.PointerPressedEvent, OnKeyPointerPressed, RoutingStrategies.Tunnel);
            AddHandler(InputElement.PointerMovedEvent, OnKeyPointerMoved, RoutingStrategies.Bubble);
            AddHandler(InputElement.PointerReleasedEvent, OnKeyPointerReleased, RoutingStrategies.Bubble);
            AddHandler(InputElement.PointerWheelChangedEvent, OnKeyPointerWheelChanged, RoutingStrategies.Bubble);

            if (string.IsNullOrWhiteSpace(_viewModel.SettingsPath))
            {
                var defaultPath = FindDefaultSettingsPath();
                if (!string.IsNullOrWhiteSpace(defaultPath))
                {
                    _viewModel.SetSettingsPath(defaultPath);
                }
            }
        }

        private async void OnBrowseClicked(object? sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Cherry settings.json",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Settings") { Patterns = new[] { "*.json" } }
                }
            });

            var selected = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(selected))
            {
                _viewModel.SetSettingsPath(selected);
            }
        }

        private async void OnLoadKeyboardImageClicked(object? sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select keyboard image",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" } }
                }
            });

            var selected = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(selected))
            {
                _viewModel.SetKeyboardImage(selected);
            }
        }

        private async void OnLoadKeyboardLayoutClicked(object? sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select key layout JSON",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } }
                }
            });

            var selected = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(selected))
            {
                _viewModel.LoadKeyboardLayout(selected);
            }
        }

        private async void OnSaveKeyboardLayoutClicked(object? sender, RoutedEventArgs e)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save key layout JSON",
                DefaultExtension = "json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } }
                }
            });

            var path = file?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                _viewModel.SaveKeyboardLayout(path);
            }
        }


        private static string? FindDefaultSettingsPath()
        {
            var localSettings = System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json");
            if (File.Exists(localSettings))
            {
                return localSettings;
            }

            return null;
        }

        // Artemis-style pointer logic
        private void OnKeyPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_viewModel.SelectedTabIndex != 0)
                return;

            var source = e.Source as Control;
            if (source?.DataContext is KeyButtonViewModel key)
            {
                // Check for resize handle
                if (source is Rectangle rect && rect.Tag is string handleTag)
                {
                    _activeKey = key;
                    _activeResizeHandle = handleTag;
                    _isDragging = true;
                    e.Pointer.Capture(this);
                    e.Handled = true;
                    return;
                }
                // Otherwise, select and start drag
                foreach (var k in _viewModel.KeyButtons)
                    k.IsSelected = false;
                key.IsSelected = true;
                _activeKey = key;
                _activeResizeHandle = null;
                if (!TryGetLayoutPosition(e, out var layoutPosition))
                    return;
                _dragOffsetX = layoutPosition.X - key.X;
                _dragOffsetY = layoutPosition.Y - key.Y;
                _isDragging = true;
                e.Pointer.Capture(this);
                e.Handled = true;
            }
        }

        private void OnResizeHandlePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            OnKeyPointerPressed(sender, e);
        }

        private void OnKeyPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_activeKey == null || !_isDragging)
                return;

            if (_activeResizeHandle != null)
            {
                // Resize logic
                if (!TryGetLayoutPosition(e, out var pos))
                    return;
                double newWidth = _activeKey.Width;
                double newHeight = _activeKey.Height;
                double newX = _activeKey.X;
                double newY = _activeKey.Y;
                switch (_activeResizeHandle)
                {
                    case "TopLeft":
                        newWidth = Math.Max(MinKeySize, _activeKey.Width + (_activeKey.X - pos.X));
                        newHeight = Math.Max(MinKeySize, _activeKey.Height + (_activeKey.Y - pos.Y));
                        newX = pos.X;
                        newY = pos.Y;
                        break;
                    case "TopRight":
                        newWidth = Math.Max(MinKeySize, pos.X - _activeKey.X);
                        newHeight = Math.Max(MinKeySize, _activeKey.Height + (_activeKey.Y - pos.Y));
                        newY = pos.Y;
                        break;
                    case "BottomLeft":
                        newWidth = Math.Max(MinKeySize, _activeKey.Width + (_activeKey.X - pos.X));
                        newX = pos.X;
                        newHeight = Math.Max(MinKeySize, pos.Y - _activeKey.Y);
                        break;
                    case "BottomRight":
                        newWidth = Math.Max(MinKeySize, pos.X - _activeKey.X);
                        newHeight = Math.Max(MinKeySize, pos.Y - _activeKey.Y);
                        break;
                }
                _activeKey.Width = Math.Min(newWidth, _viewModel.KeyboardCanvasWidth);
                _activeKey.Height = Math.Min(newHeight, _viewModel.KeyboardCanvasHeight);
                _activeKey.X = Math.Clamp(newX, 0, _viewModel.KeyboardCanvasWidth - _activeKey.Width);
                _activeKey.Y = Math.Clamp(newY, 0, _viewModel.KeyboardCanvasHeight - _activeKey.Height);
                e.Handled = true;
                return;
            }

            // Drag logic
            if (!TryGetLayoutPosition(e, out var position))
                return;
            var newXDrag = position.X - _dragOffsetX;
            var newYDrag = position.Y - _dragOffsetY;
            _activeKey.X = Math.Clamp(newXDrag, 0, _viewModel.KeyboardCanvasWidth - _activeKey.Width);
            _activeKey.Y = Math.Clamp(newYDrag, 0, _viewModel.KeyboardCanvasHeight - _activeKey.Height);
            e.Handled = true;
        }

        private void OnKeyPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_activeKey == null)
                return;
            _isDragging = false;
            _activeResizeHandle = null;
            e.Pointer.Capture(null);
            _activeKey = null;
            e.Handled = true;
        }

        private void OnKeyPointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (_viewModel.SelectedTabIndex != 0)
            {
                return;
            }

            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                return;
            }

            if (e.Source is not Control { DataContext: KeyButtonViewModel key })
            {
                return;
            }

            var delta = e.Delta.Y > 0 ? ResizeStep : -ResizeStep;
            key.Width = Math.Max(MinKeySize, key.Width + delta);
            key.Height = Math.Max(MinKeySize, key.Height + delta);
            if (_viewModel.KeyboardCanvasWidth > 0)
            {
                key.Width = Math.Min(key.Width, _viewModel.KeyboardCanvasWidth);
            }
            if (_viewModel.KeyboardCanvasHeight > 0)
            {
                key.Height = Math.Min(key.Height, _viewModel.KeyboardCanvasHeight);
            }
            ClampKeyToCanvas(key);
            e.Handled = true;
        }

        private bool TryGetLayoutPosition(PointerEventArgs e, out Point position)
        {
            position = default;

            if (_keyboardViewbox == null)
            {
                return false;
            }

            var viewboxBounds = _keyboardViewbox.Bounds;
            if (viewboxBounds.Width <= 0 || viewboxBounds.Height <= 0
                || _viewModel.KeyboardCanvasWidth <= 0 || _viewModel.KeyboardCanvasHeight <= 0)
            {
                position = e.GetPosition(_keyboardViewbox);
                return true;
            }

            var scale = Math.Min(viewboxBounds.Width / _viewModel.KeyboardCanvasWidth,
                viewboxBounds.Height / _viewModel.KeyboardCanvasHeight);
            if (scale <= 0)
            {
                position = e.GetPosition(_keyboardViewbox);
                return true;
            }

            var offsetX = (viewboxBounds.Width - _viewModel.KeyboardCanvasWidth * scale) / 2;
            var offsetY = (viewboxBounds.Height - _viewModel.KeyboardCanvasHeight * scale) / 2;
            var viewboxPos = e.GetPosition(_keyboardViewbox);

            position = new Point((viewboxPos.X - offsetX) / scale, (viewboxPos.Y - offsetY) / scale);
            return true;
        }

        private void ClampKeyToCanvas(KeyButtonViewModel key)
        {
            var maxX = Math.Max(0, _viewModel.KeyboardCanvasWidth - key.Width);
            var maxY = Math.Max(0, _viewModel.KeyboardCanvasHeight - key.Height);
            key.X = Math.Clamp(key.X, 0, maxX);
            key.Y = Math.Clamp(key.Y, 0, maxY);
        }

        private void OnDragPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }

        private void OnRunningAppsButtonClicked(object? sender, RoutedEventArgs e)
        {
            _viewModel.RefreshRunningApps();
        }
    }
}
