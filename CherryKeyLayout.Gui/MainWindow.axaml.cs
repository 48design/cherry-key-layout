using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Controls.Shapes;
using SkiaSharp;
using Svg.Skia;
using CherryKeyLayout;
using CherryKeyLayout.Gui.Services;
using CherryKeyLayout.Gui.ViewModels;

namespace CherryKeyLayout.Gui
{
    public sealed partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private KeyButtonViewModel? _activeKey;
        private string? _activeResizeHandle;
        private bool _isDragging;
        private Viewbox? _keyboardViewbox;
        private Viewbox? _profileViewbox;
        private Border? _deviceMarquee;
        private Border? _profileMarquee;
        private Button? _trashButton;
        private Border? _selectionOutline;
        private Rectangle? _selectionHandleTopLeft;
        private Rectangle? _selectionHandleTopRight;
        private Rectangle? _selectionHandleBottomLeft;
        private Rectangle? _selectionHandleBottomRight;
        private bool _isMarqueeSelecting;
        private bool _marqueeAdditive;
        private Point _marqueeStart;
        private HashSet<KeyButtonViewModel> _marqueeSeedSelection = new();
        private MarqueeContext _marqueeContext = MarqueeContext.None;
        private Point _dragStartPointer;
        private Rect _dragStartBounds;
        private Dictionary<KeyButtonViewModel, Point> _dragStartPositions = new();
        private Rect _resizeStartBounds;
        private Point _resizeStartPointer;
        private Dictionary<KeyButtonViewModel, KeySnapshot> _resizeStartStates = new();
        private readonly Stack<KeyLayoutSnapshot> _undoStack = new();
        private readonly Stack<KeyLayoutSnapshot> _redoStack = new();
        private KeyLayoutSnapshot? _pendingHistory;
        private List<ClipboardKeySnapshot> _clipboardKeys = new();
        private KeyButtonViewModel? _renameTarget;
        private bool _isRenamingKey;
        private const double ResizeStep = 2.0;
        private const double MinKeySize = 6.0;

        private enum MarqueeContext
        {
            None,
            DeviceEditor,
            ProfileEditor
        }

        private sealed record KeySnapshot(int Index, double X, double Y, double Width, double Height, bool IsSelected);

        private sealed record KeyLayoutSnapshot(IReadOnlyList<KeySnapshot> Keys);

        private sealed record ClipboardKeySnapshot(string Id, double X, double Y, double Width, double Height);

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainWindowViewModel();
            DataContext = _viewModel;
            Closed += (_, __) => _viewModel.Dispose();
            
            _keyboardViewbox = this.FindControl<Viewbox>("KeyboardViewbox");
            _profileViewbox = this.FindControl<Viewbox>("ProfileViewbox");
            _deviceMarquee = this.FindControl<Border>("DeviceMarquee");
            _profileMarquee = this.FindControl<Border>("ProfileMarquee");
            _trashButton = this.FindControl<Button>("TrashButton");
            _selectionOutline = this.FindControl<Border>("SelectionOutline");
            _selectionHandleTopLeft = this.FindControl<Rectangle>("SelectionHandleTopLeft");
            _selectionHandleTopRight = this.FindControl<Rectangle>("SelectionHandleTopRight");
            _selectionHandleBottomLeft = this.FindControl<Rectangle>("SelectionHandleBottomLeft");
            _selectionHandleBottomRight = this.FindControl<Rectangle>("SelectionHandleBottomRight");
            AddHandler(InputElement.PointerPressedEvent, OnKeyPointerPressed, RoutingStrategies.Tunnel, true);
            AddHandler(InputElement.PointerMovedEvent, OnKeyPointerMoved, RoutingStrategies.Bubble);
            AddHandler(InputElement.PointerReleasedEvent, OnKeyPointerReleased, RoutingStrategies.Bubble);
            AddHandler(InputElement.PointerWheelChangedEvent, OnKeyPointerWheelChanged, RoutingStrategies.Bubble);
            AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble);

            if (string.IsNullOrWhiteSpace(_viewModel.SettingsPath))
            {
                var defaultPath = FindDefaultSettingsPath();
                if (!string.IsNullOrWhiteSpace(defaultPath))
                {
                    _viewModel.SetSettingsPath(defaultPath);
                }
            }
        }

        private const string AboutWebsiteUrl = "https://48design.com";
        private const string AboutDonateUrl = "https://donate.48design.de/";
        private const string AboutRepoUrl = "https://github.com/48design/cherry-key-layout";

        private void OnAboutClicked(object? sender, RoutedEventArgs e)
        {
            var dialog = new Window
            {
                Title = "About Cherry Key Layout",
                Width = 760,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                SystemDecorations = SystemDecorations.None,
                ExtendClientAreaToDecorationsHint = true,
                ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome
            };

            dialog.Content = BuildAboutContent(dialog);
            dialog.ShowDialog(this);
        }

        private Control BuildAboutContent(Window dialog)
        {
            var layout = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                RowSpacing = 16,
                Margin = new Thickness(18)
            };

            var contentGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                ColumnSpacing = 16,
                RowSpacing = 16
            };

            var leftColumn = new StackPanel { Spacing = 16 };
            leftColumn.Children.Add(CreateCompanySection());
            leftColumn.Children.Add(CreateLinksSection());

            var rightColumn = new StackPanel { Spacing = 16 };
            rightColumn.Children.Add(CreateDependenciesSection());

            Grid.SetColumn(leftColumn, 0);
            Grid.SetColumn(rightColumn, 1);
            contentGrid.Children.Add(leftColumn);
            contentGrid.Children.Add(rightColumn);

            var closeButton = new Button
            {
                Content = "Close",
                MinWidth = 96,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            closeButton.Classes.Add("primary");
            closeButton.Click += (_, __) => dialog.Close();

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 10
            };
            buttonRow.Children.Add(closeButton);

            var header = CreateAboutHeader();
            Grid.SetRow(header, 0);
            Grid.SetRow(contentGrid, 1);
            Grid.SetRow(buttonRow, 2);
            layout.Children.Add(header);
            layout.Children.Add(contentGrid);
            layout.Children.Add(buttonRow);

            return layout;
        }

        private Control CreateAboutHeader()
        {
            var header = new Border();
            header.Classes.Add("card");
            header.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(Color.Parse("#2B3038"), 0),
                    new GradientStop(Color.Parse("#1F242C"), 1)
                }
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 16
            };

            var logo = CreateAboutLogo();
            Grid.SetColumn(logo, 0);
            grid.Children.Add(logo);

            var textStack = new StackPanel
            {
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center
            };

            var title = new TextBlock
            {
                Text = "Cherry Key Layout"
            };
            title.Classes.Add("title");

            var subtitle = new TextBlock
            {
                Text = "Keyboard layout editor for Cherry devices.",
                Foreground = GetBrush("TextMutedBrush"),
                TextWrapping = TextWrapping.Wrap
            };

            var version = new TextBlock
            {
                Text = $"Version {GetAppVersion()}",
                Foreground = Brushes.Black,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold
            };

            textStack.Children.Add(title);
            textStack.Children.Add(subtitle);
            textStack.Children.Add(new Border
            {
                Background = GetBrush("AccentSoftBrush"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = version
            });

            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            header.Child = grid;
            return header;
        }

        private Control CreateAboutLogo()
        {
            var logoBorder = new Border
            {
                Width = 72,
                Height = 72,
                CornerRadius = new CornerRadius(12),
                Background = GetBrush("SidebarBrush"),
                BorderBrush = GetBrush("BorderBrush"),
                BorderThickness = new Thickness(1)
            };

            logoBorder.Child = CreateSvgIcon(IconAssets.AppLogo, 48, 48);

            return logoBorder;
        }

        private Control CreateCompanySection()
        {
            var section = new Border();
            section.Classes.Add("card");

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 16
            };

            var signet = new Border
            {
                Width = 56,
                Height = 56,
                CornerRadius = new CornerRadius(12),
                Background = GetBrush("SidebarBrush"),
                BorderBrush = GetBrush("BorderBrush"),
                BorderThickness = new Thickness(1),
                Child = CreateSvgIcon(IconAssets.AuthorLogo, 36, 36)
            };

            var stack = new StackPanel
            {
                Spacing = 6
            };

            var title = new TextBlock
            {
                Text = "Author"
            };
            title.Classes.Add("section");

            stack.Children.Add(title);
            stack.Children.Add(new TextBlock { Text = "48DESIGN GmbH", FontWeight = FontWeight.SemiBold });
            stack.Children.Add(new TextBlock { Text = "Fabian Groß" });
            stack.Children.Add(new TextBlock { Text = "Gartenstr. 4" });
            stack.Children.Add(new TextBlock { Text = "75045 Walzbachtal" });

            Grid.SetColumn(signet, 0);
            Grid.SetColumn(stack, 1);
            grid.Children.Add(signet);
            grid.Children.Add(stack);

            section.Child = grid;
            return section;
        }

        private Control CreateLinksSection()
        {
            var section = new Border();
            section.Classes.Add("card");

            var stack = new StackPanel
            {
                Spacing = 8
            };

            var title = new TextBlock
            {
                Text = "Links"
            };
            title.Classes.Add("section");

            stack.Children.Add(title);
            stack.Children.Add(CreateLinkRow("Website", "48design.com", AboutWebsiteUrl));
            stack.Children.Add(CreateLinkRow("Donations", "donate.48design.de", AboutDonateUrl));
            stack.Children.Add(CreateLinkRow("Repository", "github.com/48design/cherry-key-layout", AboutRepoUrl));

            section.Child = stack;
            return section;
        }

        private Control CreateDependenciesSection()
        {
            var section = new Border();
            section.Classes.Add("card");

            var stack = new StackPanel
            {
                Spacing = 6
            };

            var title = new TextBlock
            {
                Text = "Dependencies"
            };
            title.Classes.Add("section");

            stack.Children.Add(title);
            stack.Children.Add(new TextBlock
            {
                Text = "Avalonia, Avalonia.Controls.ColorPicker, Avalonia.Desktop, Avalonia.Themes.Fluent, HidSharp (MIT)",
                TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(new TextBlock
            {
                Text = "MIT license texts are available in each dependency repository.",
                Foreground = GetBrush("TextMutedBrush"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            section.Child = stack;
            return section;
        }

        private Control CreateLinkRow(string label, string displayText, string url)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 12,
                VerticalAlignment = VerticalAlignment.Center
            };

            var labelBlock = new TextBlock
            {
                Text = label,
                Width = 90,
                Foreground = GetBrush("TextMutedBrush"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };

            var linkButton = new Button
            {
                Content = displayText,
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 4),
                CornerRadius = new CornerRadius(16)
            };
            linkButton.Classes.Add("ghost");
            linkButton.Click += (_, __) => OpenUrl(url);

            Grid.SetColumn(labelBlock, 0);
            Grid.SetColumn(linkButton, 1);
            grid.Children.Add(labelBlock);
            grid.Children.Add(linkButton);

            return grid;
        }

        public static Control CreateSvgIcon(string assetUri, double width, double height)
        {
            try
            {
                using var stream = OpenSvgStream(assetUri);
                var svg = new SKSvg();
                var picture = svg.Load(stream);
                if (picture == null)
                {
                    throw new InvalidOperationException("SVG picture not loaded.");
                }

                var rect = picture.CullRect;
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    throw new InvalidOperationException("SVG has invalid bounds.");
                }

                var targetWidth = (int)Math.Ceiling(width);
                var targetHeight = (int)Math.Ceiling(height);
                var scale = Math.Min((float)width / rect.Width, (float)height / rect.Height);
                if (scale <= 0)
                {
                    throw new InvalidOperationException("SVG scale invalid.");
                }

                var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(info);
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(scale);

                var dx = ((float)width / scale - rect.Width) / 2f - rect.Left;
                var dy = ((float)height / scale - rect.Height) / 2f - rect.Top;
                canvas.Translate(dx, dy);
                canvas.DrawPicture(picture);
                canvas.Flush();

                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var bitmapStream = new MemoryStream();
                data.SaveTo(bitmapStream);
                bitmapStream.Position = 0;

                var bitmap = new Bitmap(bitmapStream);
                return new Image
                {
                    Source = bitmap,
                    Width = width,
                    Height = height,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            catch
            {
                return new TextBlock
                {
                    Text = "SVG",
                    FontSize = 10,
                    Foreground = GetBrush("TextMutedBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
        }

        private static Stream OpenSvgStream(string assetUri)
        {
            var uri = new Uri(assetUri);
            if (AssetLoader.Exists(uri))
            {
                return AssetLoader.Open(uri);
            }

            var fileName = System.IO.Path.GetFileName(uri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new FileNotFoundException("SVG asset not found.", assetUri);
            }

            var fallbackPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
            if (File.Exists(fallbackPath))
            {
                return File.OpenRead(fallbackPath);
            }

            throw new FileNotFoundException("SVG asset not found.", fallbackPath);
        }

        private static string GetAppVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version == null ? "unknown" : version.ToString(3);
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private static IBrush GetBrush(string key)
        {
            return (IBrush)Application.Current!.FindResource(key)!;
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
            var pointer = e.GetCurrentPoint(this);
            if (pointer.Properties.IsRightButtonPressed)
            {
                if (_isDragging || _activeResizeHandle != null)
                {
                    CancelActiveManipulation();
                    e.Pointer.Capture(null);
                    e.Handled = true;
                }
                return;
            }

            if (IsSelectionHandle(e.Source as Control))
            {
                return;
            }

            if (_viewModel.SelectedTabIndex != 0)
            {
                if (_viewModel.SelectedTabIndex == 2)
                {
                    var profileSource = e.Source as Control;
                    if (profileSource?.DataContext is KeyButtonViewModel)
                    {
                        return;
                    }

                    if (TryStartMarqueeSelection(e, MarqueeContext.ProfileEditor))
                    {
                        return;
                    }
                }

                return;
            }

            var source = e.Source as Control;
            if (source?.DataContext is KeyButtonViewModel key)
            {
                if (_viewModel.IsMappingKeys && pointer.Properties.IsLeftButtonPressed)
                {
                    _ = _viewModel.MapKeyToCurrentIndexAsync(key);
                    e.Handled = true;
                    return;
                }

                // Check for resize handle
                if (source is Rectangle rect && rect.Tag is string handleTag)
                {
                    _activeKey = key;
                    _activeResizeHandle = handleTag;
                    _isDragging = true;
                    BeginHistoryCapture();
                    if (!TryGetLayoutPosition(e, out var resizeStartPosition))
                    {
                        return;
                    }
                    BeginResizeSelection(handleTag, resizeStartPosition);
                    e.Pointer.Capture(this);
                    e.Handled = true;
                    return;
                }
                // Otherwise, select and start drag
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    key.IsSelected = !key.IsSelected;
                    UpdateSelectionOverlay();
                    e.Handled = true;
                    return;
                }

                if (!key.IsSelected)
                {
                    ClearKeySelection();
                    key.IsSelected = true;
                }
                UpdateSelectionOverlay();
                _activeKey = key;
                _activeResizeHandle = null;
                if (!TryGetLayoutPosition(e, out var dragStartPosition))
                    return;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                {
                    BeginHistoryCapture();
                    if (!CloneSelectionForDrag())
                    {
                        return;
                    }
                }
                BeginHistoryCapture();
                BeginDragSelection(dragStartPosition);
                _isDragging = true;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            if (TryStartMarqueeSelection(e, MarqueeContext.DeviceEditor))
            {
                return;
            }
        }

        private void OnSelectionHandlePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_viewModel.SelectedTabIndex != 0)
            {
                return;
            }

            if (sender is not Rectangle { Tag: string handleTag })
            {
                return;
            }

            if (!TryGetSelectedKeys(out var selectedKeys))
            {
                return;
            }

            _activeKey = selectedKeys.FirstOrDefault();
            _activeResizeHandle = handleTag;
            _isDragging = true;
            BeginHistoryCapture();
            if (!TryGetLayoutPosition(e, out var resizeStartPosition))
            {
                return;
            }
            BeginResizeSelection(handleTag, resizeStartPosition);
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        private void OnResizeHandlePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            OnKeyPointerPressed(sender, e);
        }

        private void OnKeyPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_isDragging || _activeResizeHandle != null)
            {
                var pointer = e.GetCurrentPoint(this);
                if (pointer.Properties.IsRightButtonPressed)
                {
                    CancelActiveManipulation();
                    e.Pointer.Capture(null);
                    e.Handled = true;
                    return;
                }
            }

            if (_isMarqueeSelecting)
            {
                if (!TryGetMarqueePosition(e, out var marqueePosition))
                {
                    return;
                }

                var rect = GetMarqueeRect(_marqueeStart, marqueePosition);
                UpdateMarqueeVisual(rect);
                UpdateMarqueeSelection(rect);
                UpdateSelectionOverlay();
                e.Handled = true;
                return;
            }

            if (_activeKey == null || !_isDragging)
                return;

            if (_activeResizeHandle != null)
            {
                if (!TryGetLayoutPosition(e, out var pos))
                    return;
                ApplyResizeSelection(_activeResizeHandle, pos, e.KeyModifiers);
                UpdateSelectionOverlay();
                e.Handled = true;
                return;
            }

            // Drag logic
            if (!TryGetLayoutPosition(e, out var dragPosition))
                return;
            ApplyDragSelection(dragPosition, e.KeyModifiers);
            UpdateSelectionOverlay();
            e.Handled = true;
        }

        private void OnKeyPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_isMarqueeSelecting)
            {
                EndMarqueeSelection();
                e.Pointer.Capture(null);
                e.Handled = true;
                return;
            }

            if (_activeKey == null)
                return;
            if (_activeResizeHandle == null && _isDragging && IsPointerOverTrash(e))
            {
                BeginHistoryCapture();
                if (_viewModel.KeyButtons.Any(k => k.IsSelected))
                {
                    _viewModel.RemoveSelectedKeys();
                }
                else
                {
                    _viewModel.RemoveKey(_activeKey);
                }
                CommitHistoryCapture();
            }
            _isDragging = false;
            _activeResizeHandle = null;
            e.Pointer.Capture(null);
            _activeKey = null;
            CommitHistoryCapture();
            UpdateSelectionOverlay();
            e.Handled = true;
        }

        private void OnKeyPointerEntered(object? sender, PointerEventArgs e)
        {
            if (_viewModel.SelectedTabIndex != 0)
            {
                return;
            }

            if (sender is Control { DataContext: KeyButtonViewModel key })
            {
                key.IsHovering = true;
                _viewModel.SetHoverKey(key);
            }
        }

        private void OnKeyPointerExited(object? sender, PointerEventArgs e)
        {
            if (_viewModel.SelectedTabIndex != 0)
            {
                return;
            }

            if (sender is Control { DataContext: KeyButtonViewModel key })
            {
                key.IsHovering = false;
                _viewModel.ClearHoverKey(key);
            }
        }

        private void OnProfileKeyPointerEntered(object? sender, PointerEventArgs e)
        {
            if (_viewModel.SelectedTabIndex != 2)
            {
                return;
            }

            if (sender is Control { DataContext: KeyButtonViewModel key })
            {
                key.IsHovering = true;
            }
        }

        private void OnProfileKeyPointerExited(object? sender, PointerEventArgs e)
        {
            if (_viewModel.SelectedTabIndex != 2)
            {
                return;
            }

            if (sender is Control { DataContext: KeyButtonViewModel key })
            {
                key.IsHovering = false;
            }
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

        private void OnWindowKeyDown(object? sender, KeyEventArgs e)
        {
            if (TryHandleKeyRename(e))
            {
                e.Handled = true;
                return;
            }

            if (_viewModel.IsMappingKeys)
            {
                if (e.Key == Key.Escape)
                {
                    _ = _viewModel.CancelKeyMappingAsync();
                    e.Handled = true;
                }
                return;
            }

            if (ShouldIgnoreKeyShortcut(e))
            {
                return;
            }

            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z)
            {
                UndoLayoutChange();
                e.Handled = true;
                return;
            }

            if ((e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Y)
                || (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.Z))
            {
                RedoLayoutChange();
                e.Handled = true;
                return;
            }

            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.C)
            {
                CopySelectedKeys();
                e.Handled = true;
                return;
            }

            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.V)
            {
                PasteKeys();
                e.Handled = true;
                return;
            }

            if (_viewModel.SelectedTabIndex != 0)
            {
                return;
            }

            if (e.Key == Key.Delete)
            {
                if (TryGetSelectedKeys(out _))
                {
                    BeginHistoryCapture();
                    _viewModel.RemoveSelectedKeys();
                    CommitHistoryCapture();
                    UpdateSelectionOverlay();
                    e.Handled = true;
                }
                return;
            }

            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10.0 : 1.0;
            var delta = e.Key switch
            {
                Key.Left => new Vector(-step, 0),
                Key.Right => new Vector(step, 0),
                Key.Up => new Vector(0, -step),
                Key.Down => new Vector(0, step),
                _ => new Vector(0, 0)
            };

            if (delta == default)
            {
                return;
            }

            if (!TryGetSelectedKeys(out var selectedKeys))
            {
                return;
            }

            BeginHistoryCapture();
            ApplyDeltaToSelection(selectedKeys, delta);
            CommitHistoryCapture();
            UpdateSelectionOverlay();
            e.Handled = true;
        }

        private bool TryGetLayoutPosition(PointerEventArgs e, out Point position)
        {
            position = default;

            if (_keyboardViewbox == null)
            {
                return false;
            }

            return TryGetLayoutPosition(e, _keyboardViewbox, out position);
        }

        private bool TryGetLayoutPosition(PointerEventArgs e, Viewbox viewbox, out Point position)
        {
            position = default;

            var viewboxBounds = viewbox.Bounds;
            if (viewboxBounds.Width <= 0 || viewboxBounds.Height <= 0
                || _viewModel.KeyboardCanvasWidth <= 0 || _viewModel.KeyboardCanvasHeight <= 0)
            {
                position = e.GetPosition(viewbox);
                return true;
            }

            var scale = Math.Min(viewboxBounds.Width / _viewModel.KeyboardCanvasWidth,
                viewboxBounds.Height / _viewModel.KeyboardCanvasHeight);
            if (scale <= 0)
            {
                position = e.GetPosition(viewbox);
                return true;
            }

            var offsetX = (viewboxBounds.Width - _viewModel.KeyboardCanvasWidth * scale) / 2;
            var offsetY = (viewboxBounds.Height - _viewModel.KeyboardCanvasHeight * scale) / 2;
            var viewboxPos = e.GetPosition(viewbox);

            position = new Point((viewboxPos.X - offsetX) / scale, (viewboxPos.Y - offsetY) / scale);
            return true;
        }

        private bool TryStartMarqueeSelection(PointerPressedEventArgs e, MarqueeContext context)
        {
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return false;
            }

            var viewbox = context == MarqueeContext.DeviceEditor ? _keyboardViewbox : _profileViewbox;
            var marquee = context == MarqueeContext.DeviceEditor ? _deviceMarquee : _profileMarquee;
            if (viewbox == null || marquee == null)
            {
                return false;
            }

            if (!TryGetLayoutPosition(e, viewbox, out var position))
            {
                return false;
            }

            if (!IsPointerWithinSelectionContainer(e, context, viewbox))
            {
                return false;
            }

            BeginMarqueeSelection(context, position, e.KeyModifiers);
            e.Pointer.Capture(this);
            e.Handled = true;
            return true;
        }

        private bool IsPointerWithinViewbox(PointerEventArgs e, Viewbox viewbox)
        {
            var pos = e.GetPosition(viewbox);
            return pos.X >= 0 && pos.Y >= 0 && pos.X <= viewbox.Bounds.Width && pos.Y <= viewbox.Bounds.Height;
        }

        private bool IsPointerWithinSelectionContainer(PointerEventArgs e, MarqueeContext context, Viewbox viewbox)
        {
            return IsPointerWithinViewbox(e, viewbox);
        }

        private void BeginMarqueeSelection(MarqueeContext context, Point position, KeyModifiers modifiers)
        {
            _marqueeContext = context;
            _isMarqueeSelecting = true;
            _marqueeAdditive = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Shift);
            _marqueeStart = ClampToCanvas(position);
            _marqueeSeedSelection = _viewModel.KeyButtons.Where(key => key.IsSelected).ToHashSet();

            if (!_marqueeAdditive)
            {
                ClearKeySelection();
            }

            UpdateMarqueeVisual(new Rect(_marqueeStart, _marqueeStart));
            UpdateMarqueeSelection(new Rect(_marqueeStart, _marqueeStart));
        }

        private bool TryGetMarqueePosition(PointerEventArgs e, out Point position)
        {
            position = default;
            var viewbox = _marqueeContext == MarqueeContext.DeviceEditor ? _keyboardViewbox : _profileViewbox;
            if (viewbox == null)
            {
                return false;
            }

            return TryGetLayoutPosition(e, viewbox, out position);
        }

        private Rect GetMarqueeRect(Point start, Point current)
        {
            var minX = Math.Min(start.X, current.X);
            var maxX = Math.Max(start.X, current.X);
            var minY = Math.Min(start.Y, current.Y);
            var maxY = Math.Max(start.Y, current.Y);

            minX = Math.Clamp(minX, 0, _viewModel.KeyboardCanvasWidth);
            maxX = Math.Clamp(maxX, 0, _viewModel.KeyboardCanvasWidth);
            minY = Math.Clamp(minY, 0, _viewModel.KeyboardCanvasHeight);
            maxY = Math.Clamp(maxY, 0, _viewModel.KeyboardCanvasHeight);

            return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
        }

        private Point ClampToCanvas(Point position)
        {
            return new Point(
                Math.Clamp(position.X, 0, _viewModel.KeyboardCanvasWidth),
                Math.Clamp(position.Y, 0, _viewModel.KeyboardCanvasHeight));
        }

        private void UpdateMarqueeVisual(Rect rect)
        {
            var marquee = _marqueeContext == MarqueeContext.DeviceEditor ? _deviceMarquee : _profileMarquee;
            if (marquee == null)
            {
                return;
            }

            Canvas.SetLeft(marquee, rect.X);
            Canvas.SetTop(marquee, rect.Y);
            marquee.Width = rect.Width;
            marquee.Height = rect.Height;
            marquee.IsVisible = true;
        }

        private void UpdateMarqueeSelection(Rect rect)
        {
            var selected = _viewModel.KeyButtons
                .Where(key => rect.Intersects(new Rect(key.X, key.Y, key.Width, key.Height)))
                .ToHashSet();

            foreach (var key in _viewModel.KeyButtons)
            {
                var isSelected = _marqueeAdditive
                    ? _marqueeSeedSelection.Contains(key) || selected.Contains(key)
                    : selected.Contains(key);
                key.IsSelected = isSelected;
            }

            if (_marqueeContext == MarqueeContext.ProfileEditor)
            {
                _viewModel.ApplyProfileKeyColorCommand.RaiseCanExecuteChanged();
                _viewModel.ClearProfileKeySelectionCommand.RaiseCanExecuteChanged();
            }
            else
            {
                UpdateSelectionOverlay();
            }
        }

        private void ClearKeySelection()
        {
            foreach (var key in _viewModel.KeyButtons)
            {
                key.IsSelected = false;
                key.ShowResizeHandles = false;
            }

            if (_viewModel.SelectedTabIndex == 2)
            {
                _viewModel.ApplyProfileKeyColorCommand.RaiseCanExecuteChanged();
                _viewModel.ClearProfileKeySelectionCommand.RaiseCanExecuteChanged();
            }
            else
            {
                UpdateSelectionOverlay();
            }
        }

        private void EndMarqueeSelection()
        {
            var marquee = _marqueeContext == MarqueeContext.DeviceEditor ? _deviceMarquee : _profileMarquee;
            if (marquee != null)
            {
                marquee.IsVisible = false;
            }

            _isMarqueeSelecting = false;
            _marqueeAdditive = false;
            _marqueeContext = MarqueeContext.None;
            _marqueeSeedSelection.Clear();
            UpdateSelectionOverlay();
        }

        private void ClampKeyToCanvas(KeyButtonViewModel key)
        {
            var maxX = Math.Max(0, _viewModel.KeyboardCanvasWidth - key.Width);
            var maxY = Math.Max(0, _viewModel.KeyboardCanvasHeight - key.Height);
            key.X = Math.Clamp(key.X, 0, maxX);
            key.Y = Math.Clamp(key.Y, 0, maxY);
        }

        private bool TryGetSelectedKeys(out List<KeyButtonViewModel> selectedKeys)
        {
            selectedKeys = _viewModel.KeyButtons.Where(k => k.IsSelected).ToList();
            return selectedKeys.Count > 0;
        }

        private void BeginDragSelection(Point layoutPosition)
        {
            if (!TryGetSelectedKeys(out var selectedKeys) || _activeKey == null)
            {
                selectedKeys = new List<KeyButtonViewModel> { _activeKey! };
            }

            _dragStartPointer = layoutPosition;
            _dragStartPositions = selectedKeys.ToDictionary(key => key, key => new Point(key.X, key.Y));
            _dragStartBounds = GetSelectionBounds(selectedKeys, _dragStartPositions);
        }

        private void ApplyDragSelection(Point layoutPosition, KeyModifiers modifiers)
        {
            if (_dragStartPositions.Count == 0)
            {
                return;
            }

            var rawDelta = new Vector(layoutPosition.X - _dragStartPointer.X, layoutPosition.Y - _dragStartPointer.Y);
            if (modifiers.HasFlag(KeyModifiers.Shift))
            {
                var deltaX = Math.Abs(rawDelta.X);
                var deltaY = Math.Abs(rawDelta.Y);
                rawDelta = deltaX >= deltaY
                    ? new Vector(rawDelta.X, 0)
                    : new Vector(0, rawDelta.Y);
            }

            var delta = ClampDeltaToCanvas(_dragStartBounds, rawDelta);

            foreach (var (key, startPos) in _dragStartPositions)
            {
                key.X = startPos.X + delta.X;
                key.Y = startPos.Y + delta.Y;
            }
        }

        private void BeginResizeSelection(string handleTag, Point startPosition)
        {
            if (!TryGetSelectedKeys(out var selectedKeys) || _activeKey == null)
            {
                selectedKeys = new List<KeyButtonViewModel> { _activeKey! };
            }

            _resizeStartStates = selectedKeys.ToDictionary(
                key => key,
                key => new KeySnapshot(key.Index, key.X, key.Y, key.Width, key.Height, key.IsSelected));
            _resizeStartBounds = GetSelectionBounds(selectedKeys, _resizeStartStates);
            _resizeStartPointer = startPosition;
        }

        private void ApplyResizeSelection(string handleTag, Point position, KeyModifiers modifiers)
        {
            if (_resizeStartStates.Count == 0)
            {
                return;
            }

            var startBounds = _resizeStartBounds;
            if (startBounds.Width <= 0 || startBounds.Height <= 0)
            {
                return;
            }

            var minKeyWidth = _resizeStartStates.Values.Min(state => state.Width);
            var minKeyHeight = _resizeStartStates.Values.Min(state => state.Height);
            var minScaleX = MinKeySize / minKeyWidth;
            var minScaleY = MinKeySize / minKeyHeight;
            var minWidth = startBounds.Width * minScaleX;
            var minHeight = startBounds.Height * minScaleY;

            var newLeft = startBounds.Left;
            var newTop = startBounds.Top;
            var newRight = startBounds.Right;
            var newBottom = startBounds.Bottom;

            switch (handleTag)
            {
                case "TopLeft":
                    newLeft = position.X;
                    newTop = position.Y;
                    break;
                case "TopRight":
                    newRight = position.X;
                    newTop = position.Y;
                    break;
                case "BottomLeft":
                    newLeft = position.X;
                    newBottom = position.Y;
                    break;
                case "BottomRight":
                    newRight = position.X;
                    newBottom = position.Y;
                    break;
            }

            var lockHorizontal = false;
            var lockVertical = false;
            if (modifiers.HasFlag(KeyModifiers.Shift))
            {
                var deltaX = Math.Abs(position.X - _resizeStartPointer.X);
                var deltaY = Math.Abs(position.Y - _resizeStartPointer.Y);
                if (deltaX >= deltaY)
                {
                    lockVertical = true;
                }
                else
                {
                    lockHorizontal = true;
                }
            }

            if (lockVertical)
            {
                newTop = startBounds.Top;
                newBottom = startBounds.Bottom;
            }
            else if (lockHorizontal)
            {
                newLeft = startBounds.Left;
                newRight = startBounds.Right;
            }

            if (!lockHorizontal && handleTag is "TopLeft" or "BottomLeft")
            {
                newLeft = Math.Clamp(newLeft, 0, startBounds.Right - minWidth);
                newRight = startBounds.Right;
            }
            else if (!lockHorizontal)
            {
                newRight = Math.Clamp(newRight, startBounds.Left + minWidth, _viewModel.KeyboardCanvasWidth);
                newLeft = startBounds.Left;
            }

            if (!lockVertical && handleTag is "TopLeft" or "TopRight")
            {
                newTop = Math.Clamp(newTop, 0, startBounds.Bottom - minHeight);
                newBottom = startBounds.Bottom;
            }
            else if (!lockVertical)
            {
                newBottom = Math.Clamp(newBottom, startBounds.Top + minHeight, _viewModel.KeyboardCanvasHeight);
                newTop = startBounds.Top;
            }

            var newWidth = Math.Max(minWidth, newRight - newLeft);
            var newHeight = Math.Max(minHeight, newBottom - newTop);
            var scaleX = newWidth / startBounds.Width;
            var scaleY = newHeight / startBounds.Height;

            foreach (var (key, snapshot) in _resizeStartStates)
            {
                var relativeX = snapshot.X - startBounds.Left;
                var relativeY = snapshot.Y - startBounds.Top;
                var scaledWidth = Math.Max(MinKeySize, snapshot.Width * scaleX);
                var scaledHeight = Math.Max(MinKeySize, snapshot.Height * scaleY);
                var newX = newLeft + relativeX * scaleX;
                var newY = newTop + relativeY * scaleY;

                key.Width = Math.Min(scaledWidth, _viewModel.KeyboardCanvasWidth);
                key.Height = Math.Min(scaledHeight, _viewModel.KeyboardCanvasHeight);
                key.X = Math.Clamp(newX, 0, _viewModel.KeyboardCanvasWidth - key.Width);
                key.Y = Math.Clamp(newY, 0, _viewModel.KeyboardCanvasHeight - key.Height);
            }
        }

        private Rect GetSelectionBounds(IEnumerable<KeyButtonViewModel> keys, IDictionary<KeyButtonViewModel, Point> positions)
        {
            var bounds = new Rect();
            var first = true;
            foreach (var key in keys)
            {
                if (!positions.TryGetValue(key, out var pos))
                {
                    pos = new Point(key.X, key.Y);
                }

                var keyRect = new Rect(pos.X, pos.Y, key.Width, key.Height);
                bounds = first ? keyRect : bounds.Union(keyRect);
                first = false;
            }

            return bounds;
        }

        private Rect GetSelectionBounds(IEnumerable<KeyButtonViewModel> keys, IDictionary<KeyButtonViewModel, KeySnapshot> states)
        {
            var bounds = new Rect();
            var first = true;
            foreach (var key in keys)
            {
                if (!states.TryGetValue(key, out var snapshot))
                {
                    snapshot = new KeySnapshot(key.Index, key.X, key.Y, key.Width, key.Height, key.IsSelected);
                }

                var keyRect = new Rect(snapshot.X, snapshot.Y, snapshot.Width, snapshot.Height);
                bounds = first ? keyRect : bounds.Union(keyRect);
                first = false;
            }

            return bounds;
        }

        private Vector ClampDeltaToCanvas(Rect startBounds, Vector delta)
        {
            var dx = delta.X;
            var dy = delta.Y;

            if (startBounds.Left + dx < 0)
            {
                dx = -startBounds.Left;
            }
            else if (startBounds.Right + dx > _viewModel.KeyboardCanvasWidth)
            {
                dx = _viewModel.KeyboardCanvasWidth - startBounds.Right;
            }

            if (startBounds.Top + dy < 0)
            {
                dy = -startBounds.Top;
            }
            else if (startBounds.Bottom + dy > _viewModel.KeyboardCanvasHeight)
            {
                dy = _viewModel.KeyboardCanvasHeight - startBounds.Bottom;
            }

            return new Vector(dx, dy);
        }

        private void ApplyDeltaToSelection(IReadOnlyCollection<KeyButtonViewModel> keys, Vector delta)
        {
            var startPositions = keys.ToDictionary(key => key, key => new Point(key.X, key.Y));
            var bounds = GetSelectionBounds(keys, startPositions);
            var clampedDelta = ClampDeltaToCanvas(bounds, delta);

            foreach (var (key, startPos) in startPositions)
            {
                key.X = startPos.X + clampedDelta.X;
                key.Y = startPos.Y + clampedDelta.Y;
            }
        }

        private bool ShouldIgnoreKeyShortcut(KeyEventArgs e)
        {
            return e.Source is TextBox
                   || e.Source is ComboBox
                   || e.Source is ListBox
                   || e.Source is ListBoxItem;
        }

        private void BeginHistoryCapture()
        {
            _pendingHistory ??= CaptureLayoutSnapshot();
        }

        private void CommitHistoryCapture()
        {
            if (_pendingHistory == null)
            {
                return;
            }

            if (HasLayoutChanged(_pendingHistory))
            {
                _undoStack.Push(_pendingHistory);
                _redoStack.Clear();
            }

            _pendingHistory = null;
        }

        private void UndoLayoutChange()
        {
            if (_undoStack.Count == 0)
            {
                return;
            }

            var current = CaptureLayoutSnapshot();
            var snapshot = _undoStack.Pop();
            _redoStack.Push(current);
            ApplyLayoutSnapshot(snapshot);
        }

        private void RedoLayoutChange()
        {
            if (_redoStack.Count == 0)
            {
                return;
            }

            var current = CaptureLayoutSnapshot();
            var snapshot = _redoStack.Pop();
            _undoStack.Push(current);
            ApplyLayoutSnapshot(snapshot);
        }

        private KeyLayoutSnapshot CaptureLayoutSnapshot()
        {
            var keys = _viewModel.KeyButtons
                .Select(k => new KeySnapshot(k.Index, k.X, k.Y, k.Width, k.Height, k.IsSelected))
                .ToList();
            return new KeyLayoutSnapshot(keys);
        }

        private bool HasLayoutChanged(KeyLayoutSnapshot snapshot)
        {
            var map = snapshot.Keys.ToDictionary(k => k.Index);
            foreach (var key in _viewModel.KeyButtons)
            {
                if (!map.TryGetValue(key.Index, out var state))
                {
                    return true;
                }

                if (!AreClose(key.X, state.X)
                    || !AreClose(key.Y, state.Y)
                    || !AreClose(key.Width, state.Width)
                    || !AreClose(key.Height, state.Height)
                    || key.IsSelected != state.IsSelected)
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyLayoutSnapshot(KeyLayoutSnapshot snapshot)
        {
            var map = snapshot.Keys.ToDictionary(k => k.Index);
            foreach (var key in _viewModel.KeyButtons)
            {
                if (!map.TryGetValue(key.Index, out var state))
                {
                    continue;
                }

                key.X = state.X;
                key.Y = state.Y;
                key.Width = state.Width;
                key.Height = state.Height;
                key.IsSelected = state.IsSelected;
            }
            UpdateSelectionOverlay();
        }

        private static bool AreClose(double left, double right)
        {
            return Math.Abs(left - right) < 0.001;
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

        private void OnAddKeyClicked(object? sender, RoutedEventArgs e)
        {
            BeginHistoryCapture();
            _viewModel.AddKeyAt(0, 0, true);
            CommitHistoryCapture();
            UpdateSelectionOverlay();
        }

        private async void OnStartKeyMappingClicked(object? sender, RoutedEventArgs e)
        {
            if (_viewModel.IsMappingKeys)
            {
                await _viewModel.CancelKeyMappingAsync();
                return;
            }

            await _viewModel.StartKeyMappingAsync();
        }

        private async void OnMappingSkipClicked(object? sender, RoutedEventArgs e)
        {
            await _viewModel.SkipMappingAsync();
        }

        private async void OnMappingBackClicked(object? sender, RoutedEventArgs e)
        {
            await _viewModel.StepBackMappingAsync();
        }

        private async void OnMappingStopClicked(object? sender, RoutedEventArgs e)
        {
            await _viewModel.CancelKeyMappingAsync();
        }

        private void OnTrashClicked(object? sender, RoutedEventArgs e)
        {
            BeginHistoryCapture();
            _viewModel.RemoveSelectedKeys();
            CommitHistoryCapture();
            UpdateSelectionOverlay();
        }

        private void OnAlignLeftClicked(object? sender, RoutedEventArgs e)
        {
            BeginHistoryCapture();
            _viewModel.AlignSelectedKeysLeft();
            CommitHistoryCapture();
            UpdateSelectionOverlay();
        }

        private void OnAlignCenterClicked(object? sender, RoutedEventArgs e)
        {
            BeginHistoryCapture();
            _viewModel.AlignSelectedKeysCenter();
            CommitHistoryCapture();
            UpdateSelectionOverlay();
        }

        private void OnAlignRightClicked(object? sender, RoutedEventArgs e)
        {
            BeginHistoryCapture();
            _viewModel.AlignSelectedKeysRight();
            CommitHistoryCapture();
            UpdateSelectionOverlay();
        }

        private void OnAlignTopClicked(object? sender, RoutedEventArgs e)
        {
            BeginHistoryCapture();
            _viewModel.AlignSelectedKeysTop();
            CommitHistoryCapture();
            UpdateSelectionOverlay();
        }

        private void OnAlignBottomClicked(object? sender, RoutedEventArgs e)
        {
            BeginHistoryCapture();
            _viewModel.AlignSelectedKeysBottom();
            CommitHistoryCapture();
            UpdateSelectionOverlay();
        }

        private void OnDistributeHorizontalClicked(object? sender, RoutedEventArgs e)
        {
            BeginHistoryCapture();
            _viewModel.DistributeSelectedKeysHorizontally();
            CommitHistoryCapture();
            UpdateSelectionOverlay();
        }

        private void OnDistributeVerticalClicked(object? sender, RoutedEventArgs e)
        {
            BeginHistoryCapture();
            _viewModel.DistributeSelectedKeysVertically();
            CommitHistoryCapture();
            UpdateSelectionOverlay();
        }

        private void OnKeyCopyClicked(object? sender, RoutedEventArgs e)
        {
            CopySelectedKeys();
        }

        private void OnKeyPasteClicked(object? sender, RoutedEventArgs e)
        {
            PasteKeys();
        }

        private void OnKeyRenameClicked(object? sender, RoutedEventArgs e)
        {
            if (!TryGetKeyFromMenuItem(sender, out var key))
            {
                return;
            }

            _renameTarget = key;
            _isRenamingKey = true;
            _viewModel.IsRenamingKey = true;
            _viewModel.RenamePrompt = $"Press a key to rename \"{key.Id}\". Esc to cancel.";
            _viewModel.SetStatusMessage($"Press a key to rename \"{key.Id}\". Esc to cancel.");
        }

        private async void OnKeyRenameManualClicked(object? sender, RoutedEventArgs e)
        {
            if (!TryGetKeyFromMenuItem(sender, out var key))
            {
                return;
            }

            var name = await PromptForKeyNameAsync(key.Id);
            if (!string.IsNullOrWhiteSpace(name))
            {
                key.Id = name;
                _viewModel.SetStatusMessage($"Key renamed to \"{name}\".");
            }
        }

        private void OnKeyDeleteClicked(object? sender, RoutedEventArgs e)
        {
            if (!TryGetKeyFromMenuItem(sender, out var key))
            {
                return;
            }

            BeginHistoryCapture();
            if (_viewModel.KeyButtons.Any(k => k.IsSelected))
            {
                _viewModel.RemoveSelectedKeys();
            }
            else
            {
                _viewModel.RemoveKey(key);
            }
            CommitHistoryCapture();
            UpdateSelectionOverlay();
        }

        private async void OnKeyRemapClicked(object? sender, RoutedEventArgs e)
        {
            if (!TryGetKeyFromMenuItem(sender, out var key))
            {
                return;
            }

            if (_viewModel.IsMappingKeys)
            {
                _viewModel.SetStatusMessage("Stop key mapping before remapping.");
                return;
            }

            if (_viewModel.SelectedDevice == null)
            {
                _viewModel.SetStatusMessage("Select a device before remapping keys.");
                return;
            }

            int? currentIndex = null;
            if (_viewModel.SelectedDevice.KeyMap.TryGetValue(key.Id, out var mappedIndex))
            {
                currentIndex = mappedIndex;
            }

            int? newIndex = null;
            try
            {
                newIndex = await PromptForHardwareIndexByPreviewAsync(key.Id, currentIndex);
            }
            finally
            {
                await _viewModel.RestoreHardwarePreviewAsync();
            }

            if (!newIndex.HasValue)
            {
                return;
            }

            if (_viewModel.TryRemapKey(key, newIndex.Value, out var message))
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    _viewModel.SetStatusMessage(message);
                }
            }
            else if (!string.IsNullOrWhiteSpace(message))
            {
                _viewModel.SetStatusMessage(message);
            }
        }

        private static bool TryGetKeyFromMenuItem(object? sender, out KeyButtonViewModel key)
        {
            key = null!;

            if (sender is not MenuItem menuItem)
            {
                return false;
            }

            if (menuItem.Parent is ContextMenu menu && menu.PlacementTarget?.DataContext is KeyButtonViewModel target)
            {
                key = target;
                return true;
            }

            if (menuItem.DataContext is KeyButtonViewModel menuItemKey)
            {
                key = menuItemKey;
                return true;
            }

            return false;
        }

        private async Task<string?> PromptForKeyNameAsync(string currentName)
        {
            var tcs = new TaskCompletionSource<string?>();
            var input = new TextBox
            {
                Width = 220,
                Text = currentName
            };

            var okButton = new Button { Content = "OK", MinWidth = 70 };
            var cancelButton = new Button { Content = "Cancel", MinWidth = 70 };

            okButton.Click += (_, __) =>
            {
                tcs.TrySetResult(input.Text?.Trim());
            };

            cancelButton.Click += (_, __) =>
            {
                tcs.TrySetResult(null);
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            var layout = new StackPanel
            {
                Spacing = 10,
                Margin = new Thickness(12)
            };
            layout.Children.Add(new TextBlock
            {
                Text = "Enter key name",
                FontWeight = Avalonia.Media.FontWeight.SemiBold
            });
            layout.Children.Add(input);
            layout.Children.Add(buttons);

            var window = new Window
            {
                Title = "Set key name",
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Content = layout
            };

            void CloseWindow()
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }

            okButton.Click += (_, __) => CloseWindow();
            cancelButton.Click += (_, __) => CloseWindow();
            window.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    tcs.TrySetResult(input.Text?.Trim());
                    CloseWindow();
                }
                else if (e.Key == Key.Escape)
                {
                    tcs.TrySetResult(null);
                    CloseWindow();
                }
            };
            window.Closed += (_, __) =>
            {
                if (!tcs.Task.IsCompleted)
                {
                    tcs.TrySetResult(null);
                }
            };

            await window.ShowDialog(this);
            return await tcs.Task;
        }

        private async Task<int?> PromptForHardwareIndexByPreviewAsync(string keyId, int? currentIndex)
        {
            var tcs = new TaskCompletionSource<int?>();
            var total = CherryConstants.TotalKeys;
            var index = Math.Clamp(currentIndex ?? 0, 0, total - 1);

            var info = new TextBlock
            {
                FontWeight = FontWeight.SemiBold
            };

            var hint = new TextBlock
            {
                Text = "Use Next/Previous to highlight a hardware key, then Select.",
                Foreground = new SolidColorBrush(Color.Parse("#B0B0B0")),
                TextWrapping = TextWrapping.Wrap
            };

            var okButton = new Button { Content = "Select", MinWidth = 90 };
            var prevButton = new Button { Content = "Previous", MinWidth = 90 };
            var nextButton = new Button { Content = "Next", MinWidth = 90 };
            var cancelButton = new Button { Content = "Cancel", MinWidth = 90 };

            Window? window = null;

            void CloseWindow()
            {
                if (window?.IsVisible == true)
                {
                    window.Close();
                }
            }

            async Task UpdatePreviewAsync()
            {
                info.Text = $"Remap \"{keyId}\" -> hardware key {index + 1}/{total}";
                await _viewModel.PreviewHardwareIndexAsync(index);
            }

            async void StepIndex(int delta)
            {
                index = (index + delta + total) % total;
                await UpdatePreviewAsync();
            }

            okButton.Click += (_, __) =>
            {
                tcs.TrySetResult(index);
                CloseWindow();
            };
            prevButton.Click += (_, __) => StepIndex(-1);
            nextButton.Click += (_, __) => StepIndex(1);
            cancelButton.Click += (_, __) =>
            {
                tcs.TrySetResult(null);
                CloseWindow();
            };

            var navButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            navButtons.Children.Add(prevButton);
            navButtons.Children.Add(nextButton);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            var layout = new StackPanel
            {
                Spacing = 8,
                Margin = new Thickness(12)
            };
            layout.Children.Add(info);
            layout.Children.Add(hint);
            layout.Children.Add(navButtons);
            layout.Children.Add(buttons);

            window = new Window
            {
                Title = "Remap key",
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Content = layout
            };

            window.Opened += async (_, __) => await UpdatePreviewAsync();
            window.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    tcs.TrySetResult(index);
                    CloseWindow();
                }
                else if (e.Key == Key.Escape)
                {
                    tcs.TrySetResult(null);
                    CloseWindow();
                }
                else if (e.Key == Key.Left || e.Key == Key.Up)
                {
                    StepIndex(-1);
                }
                else if (e.Key == Key.Right || e.Key == Key.Down)
                {
                    StepIndex(1);
                }
            };
            window.Closed += (_, __) =>
            {
                if (!tcs.Task.IsCompleted)
                {
                    tcs.TrySetResult(null);
                }
            };

            await window.ShowDialog(this);
            return await tcs.Task;
        }

        private bool IsPointerOverTrash(PointerEventArgs e)
        {
            if (_trashButton == null)
            {
                return false;
            }

            var pos = e.GetPosition(_trashButton);
            return pos.X >= 0 && pos.Y >= 0
                   && pos.X <= _trashButton.Bounds.Width
                   && pos.Y <= _trashButton.Bounds.Height;
        }

        private void CancelActiveManipulation()
        {
            if (_pendingHistory != null)
            {
                ApplyLayoutSnapshot(_pendingHistory);
                _pendingHistory = null;
            }

            _isDragging = false;
            _activeResizeHandle = null;
            _activeKey = null;
            _dragStartPositions.Clear();
            _resizeStartStates.Clear();
            UpdateSelectionOverlay();
        }

        private bool CloneSelectionForDrag()
        {
            var selected = _viewModel.KeyButtons.Where(k => k.IsSelected).ToList();
            if (selected.Count == 0)
            {
                return false;
            }

            var startCount = _viewModel.KeyButtons.Count;
            var definitions = selected
                .Select(k => new KeyDefinition
                {
                    Id = k.Id,
                    X = k.X,
                    Y = k.Y,
                    Width = k.Width,
                    Height = k.Height
                })
                .ToList();

            _viewModel.AddKeys(definitions, true);

            var newKeys = _viewModel.KeyButtons.Skip(startCount).ToList();
            if (newKeys.Count == 0)
            {
                return false;
            }

            foreach (var key in _viewModel.KeyButtons)
            {
                key.IsSelected = newKeys.Contains(key);
                key.ShowResizeHandles = key.IsSelected && newKeys.Count == 1;
            }

            _activeKey = newKeys[0];
            UpdateSelectionOverlay();
            return true;
        }

        private void CopySelectedKeys()
        {
            if (_viewModel.SelectedTabIndex != 0)
            {
                return;
            }

            var selected = _viewModel.KeyButtons.Where(k => k.IsSelected).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            var minX = selected.Min(k => k.X);
            var minY = selected.Min(k => k.Y);
            _clipboardKeys = selected
                .Select(k => new ClipboardKeySnapshot(k.Id, k.X - minX, k.Y - minY, k.Width, k.Height))
                .ToList();
        }

        private void PasteKeys()
        {
            if (_viewModel.SelectedTabIndex != 0 || _clipboardKeys.Count == 0)
            {
                return;
            }

            var definitions = _clipboardKeys
                .Select(k => new KeyDefinition
                {
                    Id = k.Id,
                    X = k.X + 10,
                    Y = k.Y + 10,
                    Width = k.Width,
                    Height = k.Height
                })
                .ToList();

            BeginHistoryCapture();
            _viewModel.AddKeys(definitions, true);
            CommitHistoryCapture();
            UpdateSelectionOverlay();
        }

        private void UpdateSelectionOverlay()
        {
            if (_selectionOutline == null
                || _selectionHandleTopLeft == null
                || _selectionHandleTopRight == null
                || _selectionHandleBottomLeft == null
                || _selectionHandleBottomRight == null)
            {
                return;
            }

            if (_viewModel.SelectedTabIndex != 0)
            {
                SetSelectionOverlayVisible(false);
                return;
            }

            var selected = _viewModel.KeyButtons.Where(k => k.IsSelected).ToList();
            if (selected.Count == 0)
            {
                foreach (var key in _viewModel.KeyButtons)
                {
                    key.ShowResizeHandles = false;
                }
                SetSelectionOverlayVisible(false);
                _viewModel.RequestHoverPreviewUpdate();
                return;
            }

            if (selected.Count == 1)
            {
                foreach (var key in _viewModel.KeyButtons)
                {
                    key.ShowResizeHandles = key.IsSelected;
                }
                SetSelectionOverlayVisible(false);
                _viewModel.RequestHoverPreviewUpdate();
                return;
            }

            foreach (var key in _viewModel.KeyButtons)
            {
                key.ShowResizeHandles = false;
            }

            var bounds = GetSelectionBounds(selected, selected.ToDictionary(k => k, k => new Point(k.X, k.Y)));
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                SetSelectionOverlayVisible(false);
                _viewModel.RequestHoverPreviewUpdate();
                return;
            }

            Canvas.SetLeft(_selectionOutline, bounds.X);
            Canvas.SetTop(_selectionOutline, bounds.Y);
            _selectionOutline.Width = bounds.Width;
            _selectionOutline.Height = bounds.Height;

            SetHandlePosition(_selectionHandleTopLeft, bounds.Left, bounds.Top);
            SetHandlePosition(_selectionHandleTopRight, bounds.Right, bounds.Top);
            SetHandlePosition(_selectionHandleBottomLeft, bounds.Left, bounds.Bottom);
            SetHandlePosition(_selectionHandleBottomRight, bounds.Right, bounds.Bottom);

            SetSelectionOverlayVisible(true);
            _viewModel.RequestHoverPreviewUpdate();
        }

        private void SetSelectionOverlayVisible(bool isVisible)
        {
            if (_selectionOutline == null
                || _selectionHandleTopLeft == null
                || _selectionHandleTopRight == null
                || _selectionHandleBottomLeft == null
                || _selectionHandleBottomRight == null)
            {
                return;
            }

            _selectionOutline.IsVisible = isVisible;
            _selectionHandleTopLeft.IsVisible = isVisible;
            _selectionHandleTopRight.IsVisible = isVisible;
            _selectionHandleBottomLeft.IsVisible = isVisible;
            _selectionHandleBottomRight.IsVisible = isVisible;
        }

        private bool IsSelectionHandle(Control? control)
        {
            return control != null
                   && (ReferenceEquals(control, _selectionHandleTopLeft)
                       || ReferenceEquals(control, _selectionHandleTopRight)
                       || ReferenceEquals(control, _selectionHandleBottomLeft)
                       || ReferenceEquals(control, _selectionHandleBottomRight));
        }

        private static void SetHandlePosition(Control handle, double x, double y)
        {
            Canvas.SetLeft(handle, x - 4);
            Canvas.SetTop(handle, y - 4);
        }

        private bool TryHandleKeyRename(KeyEventArgs e)
        {
            if (!_isRenamingKey || _renameTarget == null)
            {
                return false;
            }

            if (e.Key == Key.Escape)
            {
                _isRenamingKey = false;
                _renameTarget = null;
                _viewModel.IsRenamingKey = false;
                _viewModel.RenamePrompt = string.Empty;
                _viewModel.SetStatusMessage("Key rename canceled.");
                return true;
            }

            var name = MapKeyName(e.Key);
            if (!string.IsNullOrWhiteSpace(name))
            {
                _renameTarget.Id = name;
                _viewModel.SetStatusMessage($"Key renamed to \"{name}\".");
            }

            _isRenamingKey = false;
            _renameTarget = null;
            _viewModel.IsRenamingKey = false;
            _viewModel.RenamePrompt = string.Empty;
            return true;
        }

        private static string MapKeyName(Key key)
        {
            return key switch
            {
                Key.LWin => "LWin",
                Key.RWin => "RWin",
                Key.NumLock => "NumLock",
                Key.LeftShift => "LShift",
                Key.RightShift => "RShift",
                Key.LeftCtrl => "LCtrl",
                Key.RightCtrl => "RCtrl",
                Key.LeftAlt => "LAlt",
                Key.RightAlt => "RAlt",
                _ => key.ToString()
            };
        }
    }
}
