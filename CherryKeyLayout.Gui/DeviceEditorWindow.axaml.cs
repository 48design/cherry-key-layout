using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using CherryKeyLayout.Gui.ViewModels;

namespace CherryKeyLayout.Gui
{
    /// <summary>
    /// Device Editor Window - Provides visual keyboard layout editing with drag-and-drop repositioning.
    /// 
    /// Key Features (inspired by Artemis-RGB's DeviceVisualizer):
    /// - Paint Mode: Click keys to apply colors
    /// - Move Mode: Drag keys to reposition them on the canvas
    /// - Proper pointer capture for smooth dragging
    /// - Bounds checking to keep keys within canvas
    /// - Visual feedback with hover effects
    /// - Prevents command execution during drag operations
    /// 
    /// Implementation Notes:
    /// - Uses Tunnel/Bubble routing strategies for proper event handling
    /// - Captures pointer to window (not control) for better drag tracking
    /// - Tracks isDragging state to differentiate clicks from drags
    /// - Canvas positioning uses absolute coordinates
    /// </summary>
    public sealed partial class DeviceEditorWindow : Window
    {
        private KeyButtonViewModel? _dragKey;
        private Canvas? _dragCanvas;
        private double _dragOffsetX;
        private double _dragOffsetY;
        private bool _isDragging;

        public DeviceEditorWindow()
        {
            InitializeComponent();
            AddHandler(InputElement.PointerPressedEvent, OnKeyPointerPressed, RoutingStrategies.Tunnel);
            AddHandler(InputElement.PointerMovedEvent, OnKeyPointerMoved, RoutingStrategies.Bubble);
            AddHandler(InputElement.PointerReleasedEvent, OnKeyPointerReleased, RoutingStrategies.Bubble);
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
            if (!string.IsNullOrWhiteSpace(selected) && DataContext is ViewModels.MainWindowViewModel viewModel)
            {
                viewModel.SetKeyboardImage(selected);
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
            if (!string.IsNullOrWhiteSpace(selected) && DataContext is ViewModels.MainWindowViewModel viewModel)
            {
                viewModel.LoadKeyboardLayout(selected);
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
            if (!string.IsNullOrWhiteSpace(path) && DataContext is ViewModels.MainWindowViewModel viewModel)
            {
                viewModel.SaveKeyboardLayout(path);
            }
        }

        private void OnKeyPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is not ViewModels.MainWindowViewModel viewModel)
            {
                return;
            }

            // Find the key button that was pressed
            if (e.Source is not Control { DataContext: KeyButtonViewModel key })
            {
                return;
            }

            var point = e.GetCurrentPoint(this);
            
            // Only handle drag in Move mode with left button
            if (viewModel.KeyPaintMode == "Move" && point.Properties.IsLeftButtonPressed)
            {
                // Find the canvas containing the keys
                var canvas = this.FindDescendantOfType<Canvas>();
                if (canvas == null)
                {
                    return;
                }

                // Calculate drag offset relative to canvas
                var canvasPosition = e.GetPosition(canvas);
                _dragKey = key;
                _dragCanvas = canvas;
                _dragOffsetX = canvasPosition.X - key.X;
                _dragOffsetY = canvasPosition.Y - key.Y;
                _isDragging = false; // Will be set to true once movement starts

                // Capture pointer to this window for smooth dragging
                e.Pointer.Capture(this);
                e.Handled = true;
            }
            // In other modes, let the button command handle the click
        }

        private void OnKeyPointerMoved(object? sender, PointerEventArgs e)
        {
            // Only process if we're dragging something
            if (_dragKey == null || _dragCanvas == null)
            {
                return;
            }

            // Mark as actively dragging (prevents click command from firing)
            _isDragging = true;

            // Get current position relative to canvas
            var position = e.GetPosition(_dragCanvas);
            
            // Calculate new position with offset
            var newX = position.X - _dragOffsetX;
            var newY = position.Y - _dragOffsetY;
            
            // Constrain to canvas bounds
            var maxX = _dragCanvas.Bounds.Width - _dragKey.Width;
            var maxY = _dragCanvas.Bounds.Height - _dragKey.Height;
            
            _dragKey.X = Math.Clamp(newX, 0, maxX);
            _dragKey.Y = Math.Clamp(newY, 0, maxY);
            
            e.Handled = true;
        }

        private void OnKeyPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            // Only handle if we were dragging
            if (_dragKey == null)
            {
                return;
            }
            
            // If we were actually dragging (not just a click), mark event as handled
            // to prevent the button command from firing
            if (_isDragging)
            {
                e.Handled = true;
            }
            
            // Release pointer capture
            e.Pointer.Capture(null);

            // Clean up drag state
            _dragKey = null;
            _dragCanvas = null;
            _isDragging = false;
        }
    }
}
