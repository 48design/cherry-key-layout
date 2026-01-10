using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CherryKeyLayout;
using CherryKeyLayout.Gui.Services;

namespace CherryKeyLayout.Gui.ViewModels
{
    public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
    {
        private const double ProfileKeyOpacity = 0.35;
        private const double ProfileUnsetOpacity = 0.06;
        private readonly LightingApplier _applier = new();
        private readonly ProfileAutoSwitcher _autoSwitcher;
        private readonly AppPreferences _preferences;
        private string _settingsPath = string.Empty;
        private string _statusMessage = string.Empty;
        private ProfileItemViewModel? _selectedProfile;
        private string _activeProfileTitle = "No profile loaded";
        private string _activeAppLabel = "Active app: (none)";
        private string _selectedProfileTitle = "Select a profile";
        private string _selectedProfileAppsLabel = "App links: none";
        private string[] _selectedProfileApps = Array.Empty<string>();
        private string _profileCountLabel = "0 profiles";
        private bool _autoSwitchEnabled = true;
        private bool _syncSelectedProfile = true;
        private CherryProfileInfo[] _profiles = Array.Empty<CherryProfileInfo>();
        private int _defaultProfileIndex;
        private bool _suppressSelectionApply;
        private string _defaultProfileLabel = "Default profile: (none)";
        private LightingMode _selectedLightingMode = LightingMode.Static;
        private Brightness _selectedBrightness = Brightness.Full;
        private Speed _selectedSpeed = Speed.Medium;
        private bool _lightingRainbow;
        private string _selectedColorHex = "#FF0000";
        private IBrush _selectedColorBrush = new SolidColorBrush(Color.Parse("#FF0000"));
        private Color _selectedColor = Color.Parse("#FF0000");
        private string _selectedColorR = "255";
        private string _selectedColorG = "0";
        private string _selectedColorB = "0";
        private bool _suppressColorTextUpdate;
        private Bitmap? _keyboardImage;
        private string _keyboardImagePath = string.Empty;
        private double _keyboardCanvasWidth = 800;
        private double _keyboardCanvasHeight = 300;
        private bool _autoApplyKeyColors = true;
        private KeyboardLayout? _keyboardLayout;
        private bool _isLayoutReady;
        private string _layoutColumnsText = "18";
        private string _layoutRowsText = "7";
        private string _deviceName = "Cherry MX Board";
        private ObservableCollection<DeviceItemViewModel> _devices = new();
        private DeviceItemViewModel? _selectedDevice;
        private string _profileTitleEdit = string.Empty;
        private string _profileTitleDraft = string.Empty;
        private ObservableCollection<AppLinkItemViewModel> _profileAppsEdit = new();
        private AppLinkItemViewModel? _selectedAppLink;
        private string _newAppLink = string.Empty;
        private string _keyPaintMode = "Paint";
        private string? _lastExternalAppPath;
        private bool _isSelfActiveApp;
        private bool _isEditingProfileTitle;
        private string _activeAppDirectory = string.Empty;
        private string _activeAppExe = string.Empty;
        private string _lastAppDirectory = string.Empty;
        private string _lastAppExe = string.Empty;
        private int _selectedTabIndex = 2;
        private ObservableCollection<RunningAppItemViewModel> _runningApps = new();
        private string _appLinksTitle = "App Links";
        private string _selectedLayoutTemplate = "Full Size (104-key)";
        private bool _showKeyNames;
        private bool _isRenamingKey;
        private string _renamePrompt = string.Empty;
        private KeyButtonViewModel? _hoverKey;
        private CancellationTokenSource? _hoverDebounceCts;
        private bool _hoverPreviewActive;
        private bool _isMappingKeys;
        private int _mappingIndex;
        private string _mappingPrompt = string.Empty;
        private KeyButtonViewModel? _mappingHighlight;
        private Stack<(KeyButtonViewModel Key, int Index)> _mappingHistory = new();
        private string? _activeDeviceId;

        public MainWindowViewModel()
        {
            _preferences = AppPreferences.Load();
            Profiles = new ObservableCollection<ProfileItemViewModel>();
            KeyButtons = new ObservableCollection<KeyButtonViewModel>();
            ReloadCommand = new DelegateCommand(_ => ReloadProfiles(), _ => CanReload());
            SetDefaultCommand = new DelegateCommand(_ => SetDefaultProfile(), _ => CanSetDefaultProfile());
            ApplyLightingCommand = new DelegateCommand(async _ => await ApplyLightingAsync(), _ => CanApplyLighting());
            KeyClickedCommand = new DelegateCommand(async param => await ApplyKeyColorAsync(param), _ => CanApplyKeyColor());
            ClearKeyColorsCommand = new DelegateCommand(async _ => await ClearKeyColorsAsync(), _ => CanApplyKeyColor());
            GenerateLayoutCommand = new DelegateCommand(_ => GenerateLayoutFromGrid(), _ => CanGenerateLayout());
            GenerateFromTemplateCommand = new DelegateCommand(_ => GenerateLayoutFromTemplate(), _ => !string.IsNullOrEmpty(_selectedLayoutTemplate));
            SaveProfileEditsCommand = new DelegateCommand(_ => SaveProfileEdits(), _ => CanSaveProfileEdits());
            AddAppLinkCommand = new DelegateCommand(_ => AddAppLink(), _ => CanAddAppLink());
            RemoveAppLinkCommand = new DelegateCommand(RemoveAppLink, CanRemoveAppLink);
            StripAppLinkCommand = new DelegateCommand(StripAppLink, CanStripAppLink);
            RefreshRunningAppsCommand = new DelegateCommand(_ => RefreshRunningApps());
            SelectRunningAppCommand = new DelegateCommand(SelectRunningApp, CanSelectRunningApp);
            ApplyKeyColorsCommand = new DelegateCommand(async _ => await ApplyKeyColorsToDeviceAsync(), _ => CanApplyKeyColor());
            AddDeviceCommand = new DelegateCommand(_ => AddDevice());
            RemoveDeviceCommand = new DelegateCommand(_ => RemoveDevice(), _ => CanRemoveDevice());
            AddLastActiveAppCommand = new DelegateCommand(_ => AddLastActiveApp(false), _ => CanAddLastActiveApp());
            AddLastActiveAppExeCommand = new DelegateCommand(_ => AddLastActiveApp(true), _ => CanAddLastActiveApp());
            SelectDeviceTabCommand = new DelegateCommand(_ => SelectedTabIndex = 0);
            SelectSettingsTabCommand = new DelegateCommand(_ => SelectedTabIndex = 1);
            StartEditProfileTitleCommand = new DelegateCommand(_ => StartEditProfileTitle(), _ => CanEditProfileTitle());
            ConfirmEditProfileTitleCommand = new DelegateCommand(_ => ConfirmEditProfileTitle(), _ => CanEditProfileTitle());
            CancelEditProfileTitleCommand = new DelegateCommand(_ => CancelEditProfileTitle(), _ => IsEditingProfileTitle);
            ProfileKeyClickedCommand = new DelegateCommand(OnProfileKeyClicked, CanProfileKeyClick);
            ApplyProfileKeyColorCommand = new DelegateCommand(_ => ApplyProfileKeyColor(), _ => CanApplyProfileKeyColor());
            ClearProfileKeySelectionCommand = new DelegateCommand(_ => ClearProfileKeySelection(), _ => CanClearProfileKeySelection());

            _autoSwitcher = new ProfileAutoSwitcher(
                ActiveAppTracker.GetActiveProcessPath,
                ApplyProfileAsyncInternal);
            _autoSwitcher.ActiveProfileChanged += OnActiveProfileChanged;

            // TODO: Custom animations would run via Custom mode + per-frame HID updates.
            LightingModes = new[]
            {
                LightingMode.SingleKey,
                LightingMode.Wave,
                LightingMode.Spectrum,
                LightingMode.Breathing,
                LightingMode.Static,
                LightingMode.Rolling,
                LightingMode.Curve,
                LightingMode.Scan,
                LightingMode.Radiation,
                LightingMode.Ripples,
                LightingMode.Custom
            };
            BrightnessOptions = Enum.GetValues<Brightness>();
            SpeedOptions = Enum.GetValues<Speed>();

            InitializeDevices();

            if (!string.IsNullOrWhiteSpace(_preferences.SettingsPath) && File.Exists(_preferences.SettingsPath))
            {
                SetSettingsPath(_preferences.SettingsPath);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ProfileItemViewModel> Profiles { get; }

        public DelegateCommand ReloadCommand { get; }
        public DelegateCommand SetDefaultCommand { get; }
        public DelegateCommand ApplyLightingCommand { get; }
        public DelegateCommand KeyClickedCommand { get; }
        public DelegateCommand ClearKeyColorsCommand { get; }
        public DelegateCommand GenerateLayoutCommand { get; }
        public DelegateCommand GenerateFromTemplateCommand { get; }
        public DelegateCommand SaveProfileEditsCommand { get; }
        public DelegateCommand AddAppLinkCommand { get; }
        public DelegateCommand RemoveAppLinkCommand { get; }
        public DelegateCommand StripAppLinkCommand { get; }
        public DelegateCommand RefreshRunningAppsCommand { get; }
        public DelegateCommand SelectRunningAppCommand { get; }
        public DelegateCommand ApplyKeyColorsCommand { get; }
        public DelegateCommand AddDeviceCommand { get; }
        public DelegateCommand RemoveDeviceCommand { get; }
        public DelegateCommand AddLastActiveAppCommand { get; }
        public DelegateCommand AddLastActiveAppExeCommand { get; }
        public DelegateCommand SelectDeviceTabCommand { get; }
        public DelegateCommand SelectSettingsTabCommand { get; }
        public DelegateCommand StartEditProfileTitleCommand { get; }
        public DelegateCommand ConfirmEditProfileTitleCommand { get; }
        public DelegateCommand CancelEditProfileTitleCommand { get; }
        public DelegateCommand ProfileKeyClickedCommand { get; }
        public DelegateCommand ApplyProfileKeyColorCommand { get; }
        public DelegateCommand ClearProfileKeySelectionCommand { get; }

        public LightingMode[] LightingModes { get; }
        public Brightness[] BrightnessOptions { get; }
        public Speed[] SpeedOptions { get; }
        public string[] KeyPaintModes { get; } = new[] { "Paint", "Pick", "Flood", "Move" };
        public string[] KeyboardLayoutTemplates { get; } = new[] { "Full Size (104-key)", "TKL (87-key)", "60% (61-key)", "Custom Grid" };

        public ObservableCollection<KeyButtonViewModel> KeyButtons { get; }

        public ObservableCollection<DeviceItemViewModel> Devices
        {
            get => _devices;
            private set
            {
                if (ReferenceEquals(_devices, value))
                {
                    return;
                }

                if (_devices != null)
                {
                    _devices.CollectionChanged -= OnDevicesCollectionChanged;
                }

                _devices = value;

                if (_devices != null)
                {
                    _devices.CollectionChanged += OnDevicesCollectionChanged;
                }

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Devices)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDeviceSelector)));
            }
        }

        public bool ShowDeviceSelector => Devices.Count > 1;

        public DeviceItemViewModel? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (SetProperty(ref _selectedDevice, value))
                {
                    ApplySelectedDevice();
                    RemoveDeviceCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string SettingsPath
        {
            get => _settingsPath;
            set => SetProperty(ref _settingsPath, value);
        }

        public bool AutoSwitchEnabled
        {
            get => _autoSwitchEnabled;
            set
            {
                if (SetProperty(ref _autoSwitchEnabled, value))
                {
                    if (_autoSwitchEnabled)
                    {
                        _autoSwitcher.Start();
                    }
                    else
                    {
                        _autoSwitcher.Stop();
                    }
                }
            }
        }

        public bool SyncSelectedProfile
        {
            get => _syncSelectedProfile;
            set => SetProperty(ref _syncSelectedProfile, value);
        }

        public ProfileItemViewModel? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (SetProperty(ref _selectedProfile, value))
                {
                    UpdateSelectedProfileDetails();
                    SetDefaultCommand.RaiseCanExecuteChanged();
                    ApplyLightingCommand.RaiseCanExecuteChanged();
                    KeyClickedCommand.RaiseCanExecuteChanged();
                    ClearKeyColorsCommand.RaiseCanExecuteChanged();
                    ApplyKeyColorsCommand.RaiseCanExecuteChanged();
                    AddLastActiveAppCommand.RaiseCanExecuteChanged();
                    AddLastActiveAppExeCommand.RaiseCanExecuteChanged();

                    if (!_suppressSelectionApply && _selectedProfile != null)
                    {
                        _ = ApplySelectedProfileAsync();
                    }
                }
            }
        }

        public string ActiveProfileTitle
        {
            get => _activeProfileTitle;
            private set => SetProperty(ref _activeProfileTitle, value);
        }

        public string ActiveAppLabel
        {
            get => _activeAppLabel;
            private set => SetProperty(ref _activeAppLabel, value);
        }

        public string SelectedProfileTitle
        {
            get => _selectedProfileTitle;
            private set => SetProperty(ref _selectedProfileTitle, value);
        }

        public string SelectedProfileAppsLabel
        {
            get => _selectedProfileAppsLabel;
            private set => SetProperty(ref _selectedProfileAppsLabel, value);
        }

        public string AppLinksTitle
        {
            get => _appLinksTitle;
            private set => SetProperty(ref _appLinksTitle, value);
        }

        public string[] SelectedProfileApps
        {
            get => _selectedProfileApps;
            private set => SetProperty(ref _selectedProfileApps, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public void SetStatusMessage(string message)
        {
            StatusMessage = message;
        }

        public void SetHoverKey(KeyButtonViewModel key)
        {
            if (_hoverKey == key)
            {
                return;
            }

            _hoverKey = key;
            ScheduleHoverPreviewUpdate();
        }

        public void ClearHoverKey(KeyButtonViewModel key)
        {
            if (_hoverKey != key)
            {
                return;
            }

            _hoverKey = null;
            ScheduleHoverPreviewUpdate();
        }

        public void RequestHoverPreviewUpdate()
        {
            if (SelectedTabIndex != 0)
            {
                return;
            }

            if (IsMappingKeys)
            {
                return;
            }

            if (_hoverKey == null && !_hoverPreviewActive && !KeyButtons.Any(k => k.IsSelected))
            {
                return;
            }

            ScheduleHoverPreviewUpdate();
        }

        public async Task StartKeyMappingAsync()
        {
            if (KeyButtons.Count == 0)
            {
                StatusMessage = "Load or create a layout before mapping keys.";
                return;
            }

            if (SelectedDevice == null)
            {
                StatusMessage = "Select a device before mapping keys.";
                return;
            }

            _hoverDebounceCts?.Cancel();
            _hoverKey = null;
            _hoverPreviewActive = false;

            SelectedDevice.KeyMap.Clear();
            SaveDevicesToPreferences();
            foreach (var key in KeyButtons)
            {
                key.IsMappedKey = false;
            }

            _mappingIndex = 0;
            _mappingHistory.Clear();
            IsMappingKeys = true;
            UpdateMappingPrompt();
            ClearMappingHighlight();
            await ShowMappingIndexAsync(_mappingIndex);
        }

        public async Task CancelKeyMappingAsync()
        {
            if (!IsMappingKeys)
            {
                return;
            }

            IsMappingKeys = false;
            MappingPrompt = string.Empty;
            ClearMappingHighlight();
            foreach (var key in KeyButtons)
            {
                key.IsMappedKey = false;
            }
            StatusMessage = "Key mapping canceled.";
            await ClearHoverPreviewAsync();
        }

        public async Task MapKeyToCurrentIndexAsync(KeyButtonViewModel key)
        {
            if (!IsMappingKeys || SelectedDevice == null)
            {
                return;
            }

            SelectedDevice.KeyMap[key.Id] = _mappingIndex;
            SaveDevicesToPreferences();
            SetMappingHighlight(key);
            key.IsMappedKey = true;
            _mappingHistory.Push((key, _mappingIndex));

            _mappingIndex++;
            if (_mappingIndex >= CherryConstants.TotalKeys)
            {
                IsMappingKeys = false;
                MappingPrompt = string.Empty;
                ClearMappingHighlight();
                StatusMessage = "Key mapping complete.";
                await ClearHoverPreviewAsync();
                return;
            }

            UpdateMappingPrompt();
            await ShowMappingIndexAsync(_mappingIndex);
        }

        public async Task SkipMappingAsync()
        {
            if (!IsMappingKeys)
            {
                return;
            }

            _mappingHistory.Push((null!, _mappingIndex));
            _mappingIndex++;
            if (_mappingIndex >= CherryConstants.TotalKeys)
            {
                IsMappingKeys = false;
                MappingPrompt = string.Empty;
                ClearMappingHighlight();
                StatusMessage = "Key mapping complete.";
                await ClearHoverPreviewAsync();
                return;
            }

            UpdateMappingPrompt();
            await ShowMappingIndexAsync(_mappingIndex);
        }

        public async Task StepBackMappingAsync()
        {
            if (!IsMappingKeys || SelectedDevice == null)
            {
                return;
            }

            if (_mappingHistory.Count == 0)
            {
                return;
            }

            var (key, index) = _mappingHistory.Pop();
            _mappingIndex = index;

            if (key != null)
            {
                SelectedDevice.KeyMap.Remove(key.Id);
                key.IsMappedKey = false;
                SaveDevicesToPreferences();
            }

            ClearMappingHighlight();
            UpdateMappingPrompt();
            await ShowMappingIndexAsync(_mappingIndex);
        }

        public string ProfileCountLabel
        {
            get => _profileCountLabel;
            private set => SetProperty(ref _profileCountLabel, value);
        }

        public bool IsSelfActiveApp
        {
            get => _isSelfActiveApp;
            private set
            {
                if (SetProperty(ref _isSelfActiveApp, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowActiveAppLabel)));
                }
            }
        }

        public string? LastExternalAppPath
        {
            get => _lastExternalAppPath;
            private set
            {
                if (SetProperty(ref _lastExternalAppPath, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLastExternalApp)));
                    AddLastActiveAppCommand.RaiseCanExecuteChanged();
                    AddLastActiveAppExeCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool HasLastExternalApp => !string.IsNullOrWhiteSpace(LastExternalAppPath);

        public bool ShowActiveAppLabel => !IsSelfActiveApp;

        public string ActiveAppDirectory
        {
            get => _activeAppDirectory;
            private set => SetProperty(ref _activeAppDirectory, value);
        }

        public string ActiveAppExe
        {
            get => _activeAppExe;
            private set => SetProperty(ref _activeAppExe, value);
        }

        public string LastAppDirectory
        {
            get => _lastAppDirectory;
            private set => SetProperty(ref _lastAppDirectory, value);
        }

        public string LastAppExe
        {
            get => _lastAppExe;
            private set => SetProperty(ref _lastAppExe, value);
        }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (SetProperty(ref _selectedTabIndex, value))
                {
                    if (_selectedTabIndex == 0)
                    {
                        ScheduleHoverPreviewUpdate();
                    }
                    else if (_selectedTabIndex == 2)
                    {
                        EnsureDeviceLayoutLoaded();
                        ApplyProfileColorsFromSettings();
                    }
                    else
                    {
                        if (IsMappingKeys)
                        {
                            _ = CancelKeyMappingAsync();
                        }
                        _ = ClearHoverPreviewAsync();
                    }
                }
            }
        }


        public string DefaultProfileLabel
        {
            get => _defaultProfileLabel;
            private set => SetProperty(ref _defaultProfileLabel, value);
        }

        public string DeviceName
        {
            get => _deviceName;
            set
            {
                if (SetProperty(ref _deviceName, value))
                {
                    if (SelectedDevice != null)
                    {
                        SelectedDevice.Name = value;
                        SaveDevicesToPreferences();
                    }
                }
            }
        }

        private void EnsureDeviceLayoutLoaded()
        {
            if (Devices.Count > 0 && SelectedDevice == null)
            {
                SelectedDevice = Devices[0];
            }

            if (SelectedDevice == null)
            {
                return;
            }

            if (!string.Equals(_activeDeviceId, SelectedDevice.Id, StringComparison.Ordinal))
            {
                ApplySelectedDevice();
                return;
            }

            if (KeyboardImage == null && !string.IsNullOrWhiteSpace(SelectedDevice.ImagePath)
                && File.Exists(SelectedDevice.ImagePath))
            {
                SetKeyboardImage(SelectedDevice.ImagePath);
            }

            if (KeyButtons.Count == 0 && !string.IsNullOrWhiteSpace(SelectedDevice.LayoutPath)
                && File.Exists(SelectedDevice.LayoutPath))
            {
                LoadKeyboardLayout(SelectedDevice.LayoutPath);
            }
        }

        public string SelectedLayoutTemplate
        {
            get => _selectedLayoutTemplate;
            set
            {
                if (SetProperty(ref _selectedLayoutTemplate, value))
                {
                    GenerateFromTemplateCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool ShowKeyNames
        {
            get => _showKeyNames;
            set => SetProperty(ref _showKeyNames, value);
        }

        public bool IsRenamingKey
        {
            get => _isRenamingKey;
            set => SetProperty(ref _isRenamingKey, value);
        }

        public string RenamePrompt
        {
            get => _renamePrompt;
            set => SetProperty(ref _renamePrompt, value);
        }

        public bool IsMappingKeys
        {
            get => _isMappingKeys;
            private set => SetProperty(ref _isMappingKeys, value);
        }

        public string MappingPrompt
        {
            get => _mappingPrompt;
            private set => SetProperty(ref _mappingPrompt, value);
        }

        public LightingMode SelectedLightingMode
        {
            get => _selectedLightingMode;
            set => SetProperty(ref _selectedLightingMode, value);
        }

        public Brightness SelectedBrightness
        {
            get => _selectedBrightness;
            set => SetProperty(ref _selectedBrightness, value);
        }

        public Speed SelectedSpeed
        {
            get => _selectedSpeed;
            set => SetProperty(ref _selectedSpeed, value);
        }

        public bool LightingRainbow
        {
            get => _lightingRainbow;
            set => SetProperty(ref _lightingRainbow, value);
        }

        public string SelectedColorHex
        {
            get => _selectedColorHex;
            set
            {
                if (SetProperty(ref _selectedColorHex, value))
                {
                    if (_suppressColorTextUpdate)
                    {
                        return;
                    }

                    if (TryParseColor(value, out var parsed))
                    {
                        _suppressColorTextUpdate = true;
                        SelectedColor = parsed;
                        _suppressColorTextUpdate = false;
                    }
                }
            }
        }

        public Color SelectedColor
        {
            get => _selectedColor;
            set
            {
                if (SetProperty(ref _selectedColor, value))
                {
                    SelectedColorBrush = new SolidColorBrush(value);
                    if (!_suppressColorTextUpdate)
                    {
                        _suppressColorTextUpdate = true;
                        SelectedColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
                        SelectedColorR = value.R.ToString();
                        SelectedColorG = value.G.ToString();
                        SelectedColorB = value.B.ToString();
                        _suppressColorTextUpdate = false;
                    }
                }
            }
        }

        public string SelectedColorR
        {
            get => _selectedColorR;
            set
            {
                if (SetProperty(ref _selectedColorR, value))
                {
                    UpdateColorFromRgbText();
                }
            }
        }

        public string SelectedColorG
        {
            get => _selectedColorG;
            set
            {
                if (SetProperty(ref _selectedColorG, value))
                {
                    UpdateColorFromRgbText();
                }
            }
        }

        public string SelectedColorB
        {
            get => _selectedColorB;
            set
            {
                if (SetProperty(ref _selectedColorB, value))
                {
                    UpdateColorFromRgbText();
                }
            }
        }

        public IBrush SelectedColorBrush
        {
            get => _selectedColorBrush;
            private set => SetProperty(ref _selectedColorBrush, value);
        }

        // Profile Key Color Properties
        private string _profileKeyColorHex = "#FF0000";
        private IBrush _profileKeyColorBrush = new SolidColorBrush(Color.Parse("#FF0000"));
        private Color _profileKeyColor = Color.Parse("#FF0000");
        private bool _suppressProfileColorUpdate;

        public string ProfileKeyColorHex
        {
            get => _profileKeyColorHex;
            set
            {
                if (SetProperty(ref _profileKeyColorHex, value))
                {
                    if (_suppressProfileColorUpdate)
                    {
                        return;
                    }

                    if (Color.TryParse(value, out var parsed))
                    {
                        ProfileKeyColor = parsed;
                    }
                }
            }
        }

        public Color ProfileKeyColor
        {
            get => _profileKeyColor;
            set
            {
                if (SetProperty(ref _profileKeyColor, value))
                {
                    ProfileKeyColorBrush = new SolidColorBrush(value);
                    _suppressProfileColorUpdate = true;
                    ProfileKeyColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
                    _suppressProfileColorUpdate = false;
                }
            }
        }

        public IBrush ProfileKeyColorBrush
        {
            get => _profileKeyColorBrush;
            private set => SetProperty(ref _profileKeyColorBrush, value);
        }

        public Bitmap? KeyboardImage
        {
            get => _keyboardImage;
            private set => SetProperty(ref _keyboardImage, value);
        }

        public string KeyboardImagePath
        {
            get => _keyboardImagePath;
            private set => SetProperty(ref _keyboardImagePath, value);
        }

        public double KeyboardCanvasWidth
        {
            get => _keyboardCanvasWidth;
            private set => SetProperty(ref _keyboardCanvasWidth, value);
        }

        public double KeyboardCanvasHeight
        {
            get => _keyboardCanvasHeight;
            private set => SetProperty(ref _keyboardCanvasHeight, value);
        }

        public bool AutoApplyKeyColors
        {
            get => _autoApplyKeyColors;
            set => SetProperty(ref _autoApplyKeyColors, value);
        }

        public bool IsLayoutReady
        {
            get => _isLayoutReady;
            private set => SetProperty(ref _isLayoutReady, value);
        }

        public string LayoutColumnsText
        {
            get => _layoutColumnsText;
            set => SetProperty(ref _layoutColumnsText, value);
        }

        public string LayoutRowsText
        {
            get => _layoutRowsText;
            set => SetProperty(ref _layoutRowsText, value);
        }

        public string ProfileTitleEdit
        {
            get => _profileTitleEdit;
            set
            {
                if (SetProperty(ref _profileTitleEdit, value))
                {
                    SaveProfileEditsCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string ProfileTitleDraft
        {
            get => _profileTitleDraft;
            set => SetProperty(ref _profileTitleDraft, value);
        }

        public bool IsEditingProfileTitle
        {
            get => _isEditingProfileTitle;
            private set
            {
                if (SetProperty(ref _isEditingProfileTitle, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNotEditingProfileTitle)));
                    StartEditProfileTitleCommand.RaiseCanExecuteChanged();
                    ConfirmEditProfileTitleCommand.RaiseCanExecuteChanged();
                    CancelEditProfileTitleCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsNotEditingProfileTitle => !IsEditingProfileTitle;

        public ObservableCollection<AppLinkItemViewModel> ProfileAppsEdit
        {
            get => _profileAppsEdit;
            private set => SetProperty(ref _profileAppsEdit, value);
        }

        public AppLinkItemViewModel? SelectedAppLink
        {
            get => _selectedAppLink;
            set
            {
                if (SetProperty(ref _selectedAppLink, value))
                {
                    RemoveAppLinkCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string NewAppLink
        {
            get => _newAppLink;
            set
            {
                if (SetProperty(ref _newAppLink, value))
                {
                    AddAppLinkCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string KeyPaintMode
        {
            get => _keyPaintMode;
            set => SetProperty(ref _keyPaintMode, value);
        }

        public ObservableCollection<RunningAppItemViewModel> RunningApps
        {
            get => _runningApps;
            private set => SetProperty(ref _runningApps, value);
        }

        public void SetSettingsPath(string path)
        {
            SettingsPath = path;
            _preferences.SettingsPath = path;
            _preferences.Save();
            ReloadProfiles();
        }

        public void SetKeyboardImage(string path)
        {
            try
            {
                KeyboardImagePath = path;
                KeyboardImage = new Bitmap(path);

                // Set canvas dimensions to match image size for proper responsiveness
                KeyboardCanvasWidth = KeyboardImage.PixelSize.Width;
                KeyboardCanvasHeight = KeyboardImage.PixelSize.Height;

                if (SelectedDevice != null)
                {
                    SelectedDevice.ImagePath = path;
                    SaveDevicesToPreferences();
                }

                GenerateLayoutCommand.RaiseCanExecuteChanged();

                if (_keyboardLayout == null)
                {
                    GenerateGridLayout();
                }
                else
                {
                    ApplyKeyboardLayout(_keyboardLayout);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load image: {ex.Message}";
                KeyboardImagePath = string.Empty;
                KeyboardImage = null;
                IsLayoutReady = false;
                KeyButtons.Clear();
            }
        }

        public void LoadKeyboardLayout(string path)
        {
            try
            {
                var layout = KeyboardLayout.Load(path);
                if (KeyboardImage != null)
                {
                    var size = KeyboardImage.PixelSize;
                    if (Math.Abs(layout.Width - size.Width) > 0.5 || Math.Abs(layout.Height - size.Height) > 0.5)
                    {
                        NormalizeLayoutToImage(layout.Keys, size.Width, size.Height);
                        layout.Width = size.Width;
                        layout.Height = size.Height;
                    }
                }
                _keyboardLayout = layout;
                if (SelectedDevice != null)
                {
                    SelectedDevice.LayoutPath = path;
                    SaveDevicesToPreferences();
                }
                ApplyKeyboardLayout(layout);
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
                _keyboardLayout = null;
                KeyButtons.Clear();
                IsLayoutReady = false;
                if (SelectedDevice != null && SelectedDevice.LayoutPath == path)
                {
                    SelectedDevice.LayoutPath = string.Empty;
                    SaveDevicesToPreferences();
                }
            }
        }

        public void SaveKeyboardLayout(string path)
        {
            if (_keyboardLayout == null)
            {
                return;
            }

            _keyboardLayout = BuildLayoutFromKeys();
            _keyboardLayout.Save(path);
            if (SelectedDevice != null)
            {
                SelectedDevice.LayoutPath = path;
                SaveDevicesToPreferences();
            }
            StatusMessage = $"Layout saved to {path}.";
        }

        public void Dispose()
        {
            _autoSwitcher.Dispose();
        }

        private bool CanReload()
        {
            return !string.IsNullOrWhiteSpace(SettingsPath);
        }

        private bool CanSetDefaultProfile()
        {
            return SelectedProfile != null;
        }

        private bool CanApplyLighting()
        {
            return SelectedProfile != null && File.Exists(SettingsPath);
        }

        private bool CanApplyKeyColor()
        {
            return SelectedProfile != null && File.Exists(SettingsPath) && KeyButtons.Count > 0;
        }

        private bool CanSaveProfileEdits()
        {
            return SelectedProfile != null && File.Exists(SettingsPath);
        }

        private bool CanAddAppLink()
        {
            return !string.IsNullOrWhiteSpace(NewAppLink);
        }

        private bool CanRemoveAppLink(object? param)
        {
            return param is AppLinkItemViewModel;
        }

        private bool CanRemoveDevice()
        {
            return Devices.Count > 1 && SelectedDevice != null;
        }

        private bool CanAddLastActiveApp()
        {
            return SelectedProfile != null && !string.IsNullOrWhiteSpace(LastExternalAppPath);
        }

        private bool CanGenerateLayout()
        {
            return KeyboardImage != null;
        }

        private void OnDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDeviceSelector)));
        }


        private void ReloadProfiles()
        {
            if (string.IsNullOrWhiteSpace(SettingsPath))
            {
                StatusMessage = "Select a settings.json file to load profiles.";
                return;
            }

            try
            {
                var (selectedIndex, profiles) = CherrySettings.LoadProfiles(SettingsPath);
                _profiles = profiles;
                var defaultIndex = _preferences.DefaultProfileIndex ?? 0;
                if (defaultIndex < 0 || defaultIndex >= profiles.Length)
                {
                    defaultIndex = 0;
                    _preferences.DefaultProfileIndex = defaultIndex;
                    _preferences.Save();
                }

                _defaultProfileIndex = defaultIndex;

                Profiles.Clear();
                foreach (var profile in profiles)
                {
                    Profiles.Add(new ProfileItemViewModel(
                        profile.Index,
                        profile.Title,
                        profile.AppEnabled,
                        profile.AppPaths,
                        profile.Index == _defaultProfileIndex));
                }

                ProfileCountLabel = $"{Profiles.Count} profile(s)";
                _suppressSelectionApply = true;
                SelectedProfile = Profiles.FirstOrDefault(profile => profile.Index == selectedIndex)
                                  ?? Profiles.FirstOrDefault();
                _suppressSelectionApply = false;

                var active = Profiles.FirstOrDefault(profile => profile.Index == selectedIndex);
                ActiveProfileTitle = active?.Title ?? "No profile loaded";
                ActiveAppLabel = "Active app: (none)";
                UpdateDefaultProfileLabel();

                _autoSwitcher.UpdateProfiles(profiles, _defaultProfileIndex);
                if (AutoSwitchEnabled)
                {
                    _autoSwitcher.Start();
                }

                StatusMessage = "Profiles loaded.";
                ReloadCommand.RaiseCanExecuteChanged();
                SetDefaultCommand.RaiseCanExecuteChanged();
                ApplyLightingCommand.RaiseCanExecuteChanged();
                KeyClickedCommand.RaiseCanExecuteChanged();
                ClearKeyColorsCommand.RaiseCanExecuteChanged();
                GenerateLayoutCommand.RaiseCanExecuteChanged();
                SaveProfileEditsCommand.RaiseCanExecuteChanged();
                AddAppLinkCommand.RaiseCanExecuteChanged();
                RemoveAppLinkCommand.RaiseCanExecuteChanged();
                ApplyKeyColorsCommand.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
                Profiles.Clear();
                ProfileCountLabel = "0 profiles";
                SelectedProfile = null;
                ActiveProfileTitle = "No profile loaded";
                ActiveAppLabel = "Active app: (none)";
                DefaultProfileLabel = "Default profile: (none)";
                _autoSwitcher.Stop();
            }
        }

        private async Task ApplySelectedProfileAsync()
        {
            if (SelectedProfile == null)
            {
                return;
            }

            await ApplyProfileAsyncInternal(SelectedProfile.Index);
            ActiveProfileTitle = SelectedProfile.Title;
            ActiveAppLabel = "Active app: (manual)";
        }

        private async Task ApplyProfileAsyncInternal(int profileIndex)
        {
            try
            {
                StatusMessage = $"Applying profile {profileIndex}...";
                await _applier.ApplyProfileAsync(SettingsPath, profileIndex, SyncSelectedProfile);
                var title = Profiles.FirstOrDefault(p => p.Index == profileIndex)?.Title;
                var label = string.IsNullOrWhiteSpace(title) ? $"Profile {profileIndex + 1}" : title;
                StatusMessage = $"Profile \"{label}\" applied.";
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }

        private void UpdateSelectedProfileDetails()
        {
            if (SelectedProfile == null)
            {
                SelectedProfileTitle = "Select a profile";
                SelectedProfileAppsLabel = "App links: none";
                SelectedProfileApps = Array.Empty<string>();
                AppLinksTitle = "App Links";
                ProfileTitleDraft = string.Empty;
                IsEditingProfileTitle = false;
                return;
            }

            SelectedProfileTitle = SelectedProfile.Title;
            SelectedProfileApps = SelectedProfile.AppPaths;
            SelectedProfileAppsLabel = SelectedProfile.AppEnabled
                ? $"App links: {SelectedProfile.AppPaths.Length}"
                : string.Empty;
            AppLinksTitle = SelectedProfile.AppPaths.Length == 1
                ? "App Links (1)"
                : $"App Links ({SelectedProfile.AppPaths.Length})";

            ProfileTitleEdit = SelectedProfile.Title;
            ProfileTitleDraft = ProfileTitleEdit;
            IsEditingProfileTitle = false;
            ProfileAppsEdit = new ObservableCollection<AppLinkItemViewModel>(
                SelectedProfile.AppPaths.Select(value => new AppLinkItemViewModel(value)));
            SaveProfileEditsCommand.RaiseCanExecuteChanged();
            RemoveAppLinkCommand.RaiseCanExecuteChanged();
            ApplyProfileColorsFromSettings();
        }

        private void SetDefaultProfile()
        {
            if (SelectedProfile == null)
            {
                return;
            }

            _defaultProfileIndex = SelectedProfile.Index;
            _preferences.DefaultProfileIndex = _defaultProfileIndex;
            _preferences.Save();

            foreach (var profile in Profiles)
            {
                profile.IsDefault = profile.Index == _defaultProfileIndex;
            }

            UpdateDefaultProfileLabel();
            _autoSwitcher.UpdateProfiles(_profiles, _defaultProfileIndex);
        }

        private void SaveProfileEdits()
        {
            if (SelectedProfile == null)
            {
                return;
            }

            try
            {
                CherrySettings.SetProfileTitle(SettingsPath, SelectedProfile.Index, ProfileTitleEdit);
                CherrySettings.SetProfileApps(
                    SettingsPath,
                    SelectedProfile.Index,
                    ProfileAppsEdit.Select(item => item.Value).ToArray());
                SaveProfileColorsToSettings();

                SelectedProfile = null;
                ReloadProfiles();
                StatusMessage = "Profile updated.";
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }

        private bool CanEditProfileTitle()
        {
            return SelectedProfile != null;
        }

        private void StartEditProfileTitle()
        {
            if (SelectedProfile == null)
            {
                return;
            }

            ProfileTitleDraft = ProfileTitleEdit;
            IsEditingProfileTitle = true;
        }

        private void ConfirmEditProfileTitle()
        {
            if (!IsEditingProfileTitle)
            {
                return;
            }

            var trimmed = ProfileTitleDraft.Trim();
            if (trimmed.Length == 0)
            {
                trimmed = SelectedProfile?.Title ?? "Profile";
            }

            ProfileTitleEdit = trimmed;
            IsEditingProfileTitle = false;
            if (SelectedProfile != null)
            {
                SelectedProfile.Title = trimmed;
            }
            SelectedProfileTitle = trimmed;
        }

        private void CancelEditProfileTitle()
        {
            ProfileTitleDraft = ProfileTitleEdit;
            IsEditingProfileTitle = false;
        }

        private bool CanProfileKeyClick(object? param)
        {
            return SelectedTabIndex == 2 && SelectedProfile != null;
        }

        private void OnProfileKeyClicked(object? param)
        {
            if (param is not KeyButtonViewModel key)
            {
                return;
            }

            key.IsSelected = true;
            key.Color = ProfileKeyColor;
            SetProfileKeyBrush(key, ProfileKeyColor);
            ClearProfileKeySelectionCommand.RaiseCanExecuteChanged();
            ApplyProfileKeyColorCommand.RaiseCanExecuteChanged();
            StatusMessage = $"Painted \"{key.Id}\".";
            _ = ApplyKeyColorsToDeviceAsync();
        }

        private bool CanApplyProfileKeyColor()
        {
            return SelectedProfile != null && KeyButtons.Any(k => k.IsSelected);
        }

        private void ApplyProfileKeyColor()
        {
            if (SelectedProfile == null)
            {
                return;
            }

            foreach (var key in KeyButtons.Where(k => k.IsSelected))
            {
                key.Color = ProfileKeyColor;
                SetProfileKeyBrush(key, ProfileKeyColor);
            }

            StatusMessage = $"Applied color to {KeyButtons.Count(k => k.IsSelected)} key(s).";
            _ = ApplyKeyColorsToDeviceAsync();
        }

        private bool CanClearProfileKeySelection()
        {
            return KeyButtons.Any(k => k.IsSelected);
        }

        private void ClearProfileKeySelection()
        {
            foreach (var key in KeyButtons)
            {
                key.IsSelected = false;
            }

            ApplyProfileKeyColorCommand.RaiseCanExecuteChanged();
            ClearProfileKeySelectionCommand.RaiseCanExecuteChanged();
        }

        private void AddAppLink()
        {
            var value = NewAppLink.Trim();
            if (value.Length == 0)
            {
                return;
            }

            ProfileAppsEdit.Add(new AppLinkItemViewModel(value));
            AppLinksTitle = ProfileAppsEdit.Count == 1
                ? "App Links (1)"
                : $"App Links ({ProfileAppsEdit.Count})";
            NewAppLink = string.Empty;
            SaveProfileEditsCommand.RaiseCanExecuteChanged();
            RemoveAppLinkCommand.RaiseCanExecuteChanged();
        }

        private void AddLastActiveApp(bool exeOnly)
        {
            if (string.IsNullOrWhiteSpace(LastExternalAppPath))
            {
                return;
            }

            var value = exeOnly
                ? Path.GetFileName(LastExternalAppPath)
                : LastExternalAppPath;
            ProfileAppsEdit.Add(new AppLinkItemViewModel(value));
            AppLinksTitle = ProfileAppsEdit.Count == 1
                ? "App Links (1)"
                : $"App Links ({ProfileAppsEdit.Count})";
            SaveProfileEditsCommand.RaiseCanExecuteChanged();
            RemoveAppLinkCommand.RaiseCanExecuteChanged();
        }

        private void RemoveAppLink(object? param)
        {
            if (param is not AppLinkItemViewModel item)
            {
                return;
            }

            ProfileAppsEdit.Remove(item);
            AppLinksTitle = ProfileAppsEdit.Count == 1
                ? "App Links (1)"
                : $"App Links ({ProfileAppsEdit.Count})";
            SaveProfileEditsCommand.RaiseCanExecuteChanged();
            RemoveAppLinkCommand.RaiseCanExecuteChanged();
        }

        private bool CanStripAppLink(object? param)
        {
            return param is AppLinkItemViewModel;
        }

        private void StripAppLink(object? param)
        {
            if (param is not AppLinkItemViewModel item)
            {
                return;
            }

            var trimmed = item.Value?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return;
            }

            item.Value = Path.GetFileName(trimmed);
            SaveProfileEditsCommand.RaiseCanExecuteChanged();
            RemoveAppLinkCommand.RaiseCanExecuteChanged();
        }

        private void UpdateDefaultProfileLabel()
        {
            var profile = Profiles.FirstOrDefault(item => item.Index == _defaultProfileIndex);
            DefaultProfileLabel = profile == null
                ? "Default profile: (none)"
                : $"Default profile: {profile.Title}";
        }

        public void RefreshRunningApps()
        {
            var apps = new List<RunningAppItemViewModel>();
            var selfPath = ActiveAppTracker.GetCurrentProcessPath();

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    var title = process.MainWindowTitle;
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        continue;
                    }

                    var path = string.Empty;
                    try
                    {
                        path = process.MainModule?.FileName ?? string.Empty;
                    }
                    catch
                    {
                        path = $"{process.ProcessName}.exe";
                    }

                    if (!string.IsNullOrWhiteSpace(selfPath)
                        && string.Equals(path, selfPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    apps.Add(new RunningAppItemViewModel(title, path));
                }
                finally
                {
                    process.Dispose();
                }
            }

            RunningApps = new ObservableCollection<RunningAppItemViewModel>(
                apps.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase));
        }

        private bool CanSelectRunningApp(object? param)
        {
            return param is RunningAppItemViewModel;
        }

        private void SelectRunningApp(object? param)
        {
            if (param is not RunningAppItemViewModel item)
            {
                return;
            }

            NewAppLink = item.Path;
        }

        private async Task ApplyLightingAsync()
        {
            if (SelectedProfile == null)
            {
                return;
            }

            try
            {
                var color = SelectedColor;
                var rgb = new Rgb(color.R, color.G, color.B);
                StatusMessage = $"Applying {SelectedLightingMode} lighting...";
                await _applier.ApplyLightingAsync(
                    SettingsPath,
                    SelectedProfile.Index,
                    SelectedLightingMode,
                    rgb,
                    SelectedBrightness,
                    SelectedSpeed,
                    LightingRainbow,
                    false);
                StatusMessage = "Lighting applied.";
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }

        public void AddKeyAt(double x, double y, bool select)
        {
            var nextIndex = KeyButtons.Count == 0 ? 0 : KeyButtons.Max(k => k.Index) + 1;
            var key = new KeyDefinition
            {
                Id = $"Key {nextIndex + 1}",
                Index = nextIndex,
                X = Math.Clamp(x, 0, KeyboardCanvasWidth),
                Y = Math.Clamp(y, 0, KeyboardCanvasHeight),
                Width = 24,
                Height = 24
            };

            if (select)
            {
                foreach (var entry in KeyButtons)
                {
                    entry.IsSelected = false;
                    entry.ShowResizeHandles = false;
                }
            }

            KeyButtons.Add(new KeyButtonViewModel(key) { IsSelected = select, ShowResizeHandles = select });
            IsLayoutReady = KeyButtons.Count > 0;
            ApplyKeyColorsCommand.RaiseCanExecuteChanged();
        }

        public void RemoveSelectedKeys()
        {
            var selected = KeyButtons.Where(k => k.IsSelected).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            foreach (var key in selected)
            {
                KeyButtons.Remove(key);
            }

            IsLayoutReady = KeyButtons.Count > 0;
            ApplyKeyColorsCommand.RaiseCanExecuteChanged();
        }

        public void RemoveKey(KeyButtonViewModel key)
        {
            if (KeyButtons.Remove(key))
            {
                IsLayoutReady = KeyButtons.Count > 0;
                ApplyKeyColorsCommand.RaiseCanExecuteChanged();
            }
        }

        private void ScheduleHoverPreviewUpdate()
        {
            _hoverDebounceCts?.Cancel();
            var cts = new CancellationTokenSource();
            _hoverDebounceCts = cts;
            _ = ApplyHoverPreviewAfterDelayAsync(cts);
        }

        private async Task ClearHoverPreviewAsync()
        {
            _hoverDebounceCts?.Cancel();
            _hoverKey = null;

            if (!_hoverPreviewActive)
            {
                return;
            }

            _hoverPreviewActive = false;
            if (SelectedProfile != null && !string.IsNullOrWhiteSpace(SettingsPath))
            {
                try
                {
                    await ApplySelectedProfileAsync();
                }
                catch (Exception ex)
                {
                    StatusMessage = ex.Message;
                }
            }
        }

        private async Task ShowMappingIndexAsync(int index)
        {
            if (index < 0 || index >= CherryConstants.TotalKeys)
            {
                return;
            }

            var colors = new Rgb[CherryConstants.TotalKeys];
            colors[index] = new Rgb(255, 0, 0);

            try
            {
                var profileIndex = SelectedProfile?.Index ?? 0;
                await _applier.ApplyCustomColorsAsync(
                    SettingsPath,
                    profileIndex,
                    colors,
                    Brightness.Full,
                    Speed.Medium,
                    false);
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }

        private void UpdateMappingPrompt()
        {
            MappingPrompt = $"Mapping {_mappingIndex + 1}/{CherryConstants.TotalKeys}. Click the red key in the app. Esc cancels.";
        }

        private void SetMappingHighlight(KeyButtonViewModel key)
        {
            if (_mappingHighlight != null)
            {
                _mappingHighlight.IsMappingHighlight = false;
            }

            _mappingHighlight = key;
            _mappingHighlight.IsMappingHighlight = true;
        }

        private void ClearMappingHighlight()
        {
            if (_mappingHighlight == null)
            {
                return;
            }

            _mappingHighlight.IsMappingHighlight = false;
            _mappingHighlight = null;
        }

        private async Task ApplyHoverPreviewAfterDelayAsync(CancellationTokenSource cts)
        {
            try
            {
                await Task.Delay(25, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (cts.IsCancellationRequested)
            {
                return;
            }

            await ApplyHoverPreviewAsync(cts.Token);
        }

        private async Task ApplyHoverPreviewAsync(CancellationToken token)
        {
            if (SelectedTabIndex != 0)
            {
                return;
            }

            if (IsMappingKeys)
            {
                return;
            }

            var snapshot = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var hasHighlight = true;
                var colors = new Rgb[CherryConstants.TotalKeys];
                foreach (var key in KeyButtons)
                {
                    if (!TryGetHardwareIndex(key, out var hardwareIndex))
                    {
                        continue;
                    }

                    var previewColor = GetPreviewColor(key);
                    colors[hardwareIndex] = new Rgb(previewColor.R, previewColor.G, previewColor.B);
                }

                return (colors, hasHighlight);
            });

            if (token.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var profileIndex = SelectedProfile?.Index ?? 0;
                await _applier.ApplyCustomColorsAsync(
                    SettingsPath,
                    profileIndex,
                    snapshot.colors,
                    SelectedBrightness,
                    SelectedSpeed,
                    false);
                _hoverPreviewActive = snapshot.hasHighlight;
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }

        private Color GetPreviewColor(KeyButtonViewModel key)
        {
            if (key.IsSelected)
            {
                return Color.FromRgb(0, 191, 255);
            }

            if (_hoverKey == key)
            {
                return Color.FromRgb(255, 255, 255);
            }

            return Color.FromRgb(26, 26, 26);
        }

        private bool TryGetHardwareIndex(KeyButtonViewModel key, out int hardwareIndex)
        {
            hardwareIndex = -1;
            var map = SelectedDevice?.KeyMap;
            var hasMapping = map != null && map.Count > 0;

            if (hasMapping)
            {
                return map!.TryGetValue(key.Id, out hardwareIndex)
                       && hardwareIndex >= 0
                       && hardwareIndex < CherryConstants.TotalKeys;
            }

            return key.Index >= 0
                   && key.Index < CherryConstants.TotalKeys
                   && (hardwareIndex = key.Index) >= 0;
        }

        private void ApplyProfileColorsFromSettings()
        {
            if (SelectedProfile == null || !File.Exists(SettingsPath))
            {
                return;
            }

            CherrySettingsLighting lighting;
            try
            {
                lighting = CherrySettings.LoadLighting(SettingsPath, SelectedProfile.Index);
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
                return;
            }

            var customColors = lighting.CustomColors;
            var hasCustomColors = customColors != null && customColors.Length > 0;
            var fallbackColor = Color.FromRgb(lighting.Color.R, lighting.Color.G, lighting.Color.B);

            if (!hasCustomColors)
            {
                foreach (var key in KeyButtons)
                {
                    key.Color = fallbackColor;
                    SetProfileKeyBrush(key, fallbackColor);
                }
                return;
            }

            foreach (var key in KeyButtons)
            {
                if (!TryGetHardwareIndex(key, out var hardwareIndex))
                {
                    key.Color = Colors.Transparent;
                    key.FillBrush = new SolidColorBrush(Colors.White, ProfileUnsetOpacity);
                    continue;
                }

                if (hardwareIndex < 0 || hardwareIndex >= customColors!.Length)
                {
                    key.Color = Colors.Transparent;
                    key.FillBrush = new SolidColorBrush(Colors.White, ProfileUnsetOpacity);
                    continue;
                }

                var rgb = customColors[hardwareIndex];
                var color = Color.FromRgb(rgb.R, rgb.G, rgb.B);
                key.Color = color;
                SetProfileKeyBrush(key, color);
            }
        }

        private void SaveProfileColorsToSettings()
        {
            if (SelectedProfile == null || !File.Exists(SettingsPath))
            {
                return;
            }

            CherrySettingsLighting lighting;
            try
            {
                lighting = CherrySettings.LoadLighting(SettingsPath, SelectedProfile.Index);
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
                return;
            }

            var colors = BuildProfileCustomColors(lighting);
            lighting.Mode = LightingMode.Custom;
            lighting.CustomColors = colors;

            try
            {
                CherrySettings.SaveLighting(SettingsPath, lighting, SelectedProfile.Index);
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }

        private Rgb[] BuildProfileCustomColors(CherrySettingsLighting lighting)
        {
            var total = CherryConstants.TotalKeys;
            var colors = new Rgb[total];
            if (lighting.CustomColors != null && lighting.CustomColors.Length == total)
            {
                Array.Copy(lighting.CustomColors, colors, total);
            }

            foreach (var key in KeyButtons)
            {
                if (!TryGetHardwareIndex(key, out var hardwareIndex))
                {
                    continue;
                }

                var color = key.Color;
                colors[hardwareIndex] = new Rgb(color.R, color.G, color.B);
            }

            return colors;
        }

        private void SetProfileKeyBrush(KeyButtonViewModel key, Color color)
        {
            var opacity = GetProfileKeyOpacity(color);
            key.FillBrush = new SolidColorBrush(color, opacity);
        }

        private static double GetProfileKeyOpacity(Color color)
        {
            if (color.R == 0 && color.G == 0 && color.B == 0)
            {
                return ProfileUnsetOpacity;
            }

            return ProfileKeyOpacity;
        }


        public void AddKeys(IEnumerable<KeyDefinition> definitions, bool select)
        {
            var nextIndex = KeyButtons.Count == 0 ? 0 : KeyButtons.Max(k => k.Index) + 1;
            if (select)
            {
                foreach (var key in KeyButtons)
                {
                    key.IsSelected = false;
                    key.ShowResizeHandles = false;
                }
            }

            foreach (var definition in definitions)
            {
                var key = new KeyDefinition
                {
                    Id = definition.Id,
                    Index = nextIndex++,
                    X = Math.Clamp(definition.X, 0, KeyboardCanvasWidth),
                    Y = Math.Clamp(definition.Y, 0, KeyboardCanvasHeight),
                    Width = definition.Width,
                    Height = definition.Height
                };

                KeyButtons.Add(new KeyButtonViewModel(key) { IsSelected = select, ShowResizeHandles = select });
            }

            IsLayoutReady = KeyButtons.Count > 0;
            ApplyKeyColorsCommand.RaiseCanExecuteChanged();
        }

        public void AlignSelectedKeysLeft()
        {
            var selected = KeyButtons.Where(k => k.IsSelected).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            var minX = selected.Min(k => k.X);
            foreach (var key in selected)
            {
                key.X = Math.Clamp(minX, 0, KeyboardCanvasWidth - key.Width);
            }
        }

        public void AlignSelectedKeysCenter()
        {
            var selected = KeyButtons.Where(k => k.IsSelected).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            var minX = selected.Min(k => k.X);
            var maxX = selected.Max(k => k.X + k.Width);
            var center = (minX + maxX) / 2.0;

            foreach (var key in selected)
            {
                var targetX = center - (key.Width / 2.0);
                key.X = Math.Clamp(targetX, 0, KeyboardCanvasWidth - key.Width);
            }
        }

        public void AlignSelectedKeysRight()
        {
            var selected = KeyButtons.Where(k => k.IsSelected).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            var maxX = selected.Max(k => k.X + k.Width);
            foreach (var key in selected)
            {
                key.X = Math.Clamp(maxX - key.Width, 0, KeyboardCanvasWidth - key.Width);
            }
        }

        public void AlignSelectedKeysTop()
        {
            var selected = KeyButtons.Where(k => k.IsSelected).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            var minY = selected.Min(k => k.Y);
            foreach (var key in selected)
            {
                key.Y = Math.Clamp(minY, 0, KeyboardCanvasHeight - key.Height);
            }
        }

        public void AlignSelectedKeysBottom()
        {
            var selected = KeyButtons.Where(k => k.IsSelected).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            var maxY = selected.Max(k => k.Y + k.Height);
            foreach (var key in selected)
            {
                key.Y = Math.Clamp(maxY - key.Height, 0, KeyboardCanvasHeight - key.Height);
            }
        }

        public void DistributeSelectedKeysHorizontally()
        {
            var selected = KeyButtons.Where(k => k.IsSelected).OrderBy(k => k.X).ToList();
            if (selected.Count < 3)
            {
                return;
            }

            var minX = selected.Min(k => k.X);
            var maxRight = selected.Max(k => k.X + k.Width);
            var totalWidth = selected.Sum(k => k.Width);
            var span = maxRight - minX;
            var gap = Math.Max(0, (span - totalWidth) / (selected.Count - 1));

            var cursor = minX;
            foreach (var key in selected)
            {
                key.X = Math.Clamp(cursor, 0, KeyboardCanvasWidth - key.Width);
                cursor += key.Width + gap;
            }
        }

        public void DistributeSelectedKeysVertically()
        {
            var selected = KeyButtons.Where(k => k.IsSelected).OrderBy(k => k.Y).ToList();
            if (selected.Count < 3)
            {
                return;
            }

            var minY = selected.Min(k => k.Y);
            var maxBottom = selected.Max(k => k.Y + k.Height);
            var totalHeight = selected.Sum(k => k.Height);
            var span = maxBottom - minY;
            var gap = Math.Max(0, (span - totalHeight) / (selected.Count - 1));

            var cursor = minY;
            foreach (var key in selected)
            {
                key.Y = Math.Clamp(cursor, 0, KeyboardCanvasHeight - key.Height);
                cursor += key.Height + gap;
            }
        }

        private async Task ApplyKeyColorAsync(object? param)
        {
            if (param is not KeyButtonViewModel key)
            {
                return;
            }

            if (KeyPaintMode == "Pick")
            {
                SelectedColor = key.Color;
                return;
            }

            if (KeyPaintMode == "Flood")
            {
                foreach (var entry in KeyButtons)
                {
                    entry.SetColor(SelectedColor);
                }

                await ApplyKeyColorsToDeviceAsync();
                return;
            }

            var color = SelectedColor;
            key.SetColor(color);

            if (!AutoApplyKeyColors)
            {
                return;
            }

            await ApplyKeyColorsToDeviceAsync();
        }

        private async Task ClearKeyColorsAsync()
        {
            foreach (var key in KeyButtons)
            {
                key.SetColor(Colors.Transparent);
            }

            await ApplyKeyColorsToDeviceAsync();
        }

        private async Task ApplyKeyColorsToDeviceAsync()
        {
            if (SelectedProfile == null)
            {
                return;
            }

            var colors = new Rgb[CherryConstants.TotalKeys];
            foreach (var key in KeyButtons)
            {
                if (!TryGetHardwareIndex(key, out var hardwareIndex))
                {
                    continue;
                }

                var color = key.Color;
                colors[hardwareIndex] = new Rgb(color.R, color.G, color.B);
            }

            try
            {
                StatusMessage = "Applying custom key colors...";
            await _applier.ApplyCustomColorsAsync(
                SettingsPath,
                SelectedProfile.Index,
                colors,
                SelectedBrightness,
                SelectedSpeed,
                SyncSelectedProfile);
                StatusMessage = "Custom key colors applied.";
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }

        private void OnActiveProfileChanged(int profileIndex, string? activeAppPath)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var profile = Profiles.FirstOrDefault(item => item.Index == profileIndex);
                ActiveProfileTitle = profile?.Title ?? $"Profile {profileIndex + 1}";
                ActiveAppLabel = string.IsNullOrWhiteSpace(activeAppPath)
                    ? "Active app: (none)"
                    : $"Active app: {activeAppPath}";
                UpdateActiveAppParts(activeAppPath);

                if (IsSelfApp(activeAppPath))
                {
                    IsSelfActiveApp = true;
                }
                else
                {
                    IsSelfActiveApp = false;
                    if (!string.IsNullOrWhiteSpace(activeAppPath))
                    {
                        LastExternalAppPath = activeAppPath;
                        UpdateLastAppParts(activeAppPath);
                    }
                }
            });
        }

        private void UpdateActiveAppParts(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                ActiveAppDirectory = string.Empty;
                ActiveAppExe = string.Empty;
                return;
            }

            ActiveAppExe = Path.GetFileName(path);
            var dir = Path.GetDirectoryName(path) ?? string.Empty;
            ActiveAppDirectory = dir.Length == 0 ? string.Empty : dir + Path.DirectorySeparatorChar;
        }

        private void UpdateLastAppParts(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                LastAppDirectory = string.Empty;
                LastAppExe = string.Empty;
                return;
            }

            LastAppExe = Path.GetFileName(path);
            var dir = Path.GetDirectoryName(path) ?? string.Empty;
            LastAppDirectory = dir.Length == 0 ? string.Empty : dir + Path.DirectorySeparatorChar;
        }

        private static bool IsSelfApp(string? activeAppPath)
        {
            if (string.IsNullOrWhiteSpace(activeAppPath))
            {
                return false;
            }

            var selfPath = ActiveAppTracker.GetCurrentProcessPath();
            if (!string.IsNullOrWhiteSpace(selfPath)
                && string.Equals(activeAppPath, selfPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var activeName = Path.GetFileName(activeAppPath);
            var selfName = ActiveAppTracker.GetCurrentProcessName();
            if (!string.IsNullOrWhiteSpace(selfName))
            {
                var selfExe = selfName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? selfName
                    : selfName + ".exe";

                if (string.Equals(activeName, selfExe, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return string.Equals(activeName, "dotnet.exe", StringComparison.OrdinalIgnoreCase)
                   && string.Equals(selfName, "dotnet", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyKeyboardLayout(KeyboardLayout layout)
        {
            KeyboardCanvasWidth = layout.Width;
            KeyboardCanvasHeight = layout.Height;
            KeyButtons.Clear();
            foreach (var key in layout.Keys)
            {
                KeyButtons.Add(new KeyButtonViewModel(key));
            }
            IsLayoutReady = KeyButtons.Count > 0;
            ApplyKeyColorsCommand.RaiseCanExecuteChanged();
            if (SelectedTabIndex == 2 && SelectedProfile != null)
            {
                ApplyProfileColorsFromSettings();
            }
        }

        private void GenerateGridLayout()
        {
            if (KeyboardImage == null)
            {
                return;
            }

            var size = KeyboardImage.PixelSize;
            var layout = KeyboardLayout.GenerateGrid(size.Width, size.Height, CherryConstants.TotalKeys);
            _keyboardLayout = layout;
            ApplyKeyboardLayout(layout);
            GenerateLayoutCommand.RaiseCanExecuteChanged();
        }

        private void GenerateLayoutFromGrid()
        {
            if (KeyboardImage == null)
            {
                StatusMessage = "Load a keyboard image first.";
                return;
            }

            if (!int.TryParse(LayoutColumnsText, out var columns) || columns <= 0)
            {
                StatusMessage = "Columns must be a positive number.";
                return;
            }

            if (!int.TryParse(LayoutRowsText, out var rows) || rows <= 0)
            {
                StatusMessage = "Rows must be a positive number.";
                return;
            }

            var size = KeyboardImage.PixelSize;
            var keyWidth = size.Width / (double)columns;
            var keyHeight = size.Height / (double)rows;
            var keyCount = columns * rows;
            var keys = Enumerable.Range(0, keyCount)
                .Select(index =>
                {
                    var row = index / columns;
                    var col = index % columns;
                    return new KeyDefinition
                    {
                        Id = $"Key {index + 1}",
                        Index = index,
                        X = col * keyWidth,
                        Y = row * keyHeight,
                        Width = keyWidth,
                        Height = keyHeight
                    };
                })
                .ToArray();

            var layout = new KeyboardLayout
            {
                Width = size.Width,
                Height = size.Height,
                Keys = keys
            };

            _keyboardLayout = layout;
            ApplyKeyboardLayout(layout);
            StatusMessage = $"Generated {keyCount} keys.";
        }

        private void GenerateLayoutFromTemplate()
        {
            if (string.IsNullOrEmpty(SelectedLayoutTemplate))
            {
                StatusMessage = "Select a template first.";
                return;
            }

            KeyDefinition[] keys;
            double width, height;
            
            // Standard key unit size (19mm is typical)
            const double keyUnit = 19.0;
            const double keyGap = 1.0;
            
            switch (SelectedLayoutTemplate)
            {
                case "Full Size (104-key)":
                    keys = GenerateFullSizeKeyboard(keyUnit, keyGap);
                    break;
                
                case "TKL (87-key)":
                    keys = GenerateTKLKeyboard(keyUnit, keyGap);
                    break;
                
                case "60% (61-key)":
                    keys = Generate60PercentKeyboard(keyUnit, keyGap);
                    break;
                
                default:
                    StatusMessage = "Unknown template.";
                    return;
            }

            (width, height) = GetLayoutBounds(keys);

            if (KeyboardImage != null)
            {
                var size = KeyboardImage.PixelSize;
                NormalizeLayoutToImage(keys, size.Width, size.Height);
                width = size.Width;
                height = size.Height;
            }

            var layout = new KeyboardLayout
            {
                Width = width,
                Height = height,
                Keys = keys
            };

            _keyboardLayout = layout;
            KeyboardCanvasWidth = width;
            KeyboardCanvasHeight = height;
            ApplyKeyboardLayout(layout);
            StatusMessage = $"Generated {keys.Length}-key {SelectedLayoutTemplate} layout. Drag keys to reposition.";
        }

        private static (double Width, double Height) GetLayoutBounds(IEnumerable<KeyDefinition> keys)
        {
            double width = 0;
            double height = 0;

            foreach (var key in keys)
            {
                width = Math.Max(width, key.X + key.Width);
                height = Math.Max(height, key.Y + key.Height);
            }

            return (width, height);
        }

        private static void NormalizeLayoutToImage(KeyDefinition[] keys, int imageWidth, int imageHeight)
        {
            var (layoutWidth, layoutHeight) = GetLayoutBounds(keys);
            if (layoutWidth <= 0 || layoutHeight <= 0)
            {
                return;
            }

            var scale = Math.Min(imageWidth / layoutWidth, imageHeight / layoutHeight);
            if (scale <= 0)
            {
                return;
            }

            var offsetX = (imageWidth - layoutWidth * scale) / 2;
            var offsetY = (imageHeight - layoutHeight * scale) / 2;

            foreach (var key in keys)
            {
                key.X = key.X * scale + offsetX;
                key.Y = key.Y * scale + offsetY;
                key.Width *= scale;
                key.Height *= scale;
            }
        }

        private KeyDefinition[] GenerateFullSizeKeyboard(double unit, double gap)
        {
            var keys = new List<KeyDefinition>();
            int index = 0;

            AddMainBlock(keys, ref index, unit, gap, includeFunctionRow: true);

            var rowStep = unit + gap;
            var row0 = 0d;
            var row1 = rowStep;
            var row2 = row1 + rowStep;
            var row4 = row2 + rowStep + rowStep;
            var row5 = row4 + rowStep;

            var mainBounds = GetLayoutBounds(keys);
            var sectionGap = unit;
            var clusterX = mainBounds.Width + sectionGap;

            // System keys (top cluster)
            AddKey(keys, ref index, "PrtSc", clusterX, row0, unit, gap);
            AddKey(keys, ref index, "ScrLk", clusterX + (unit + gap), row0, unit, gap);
            AddKey(keys, ref index, "Pause", clusterX + 2 * (unit + gap), row0, unit, gap);

            // Navigation cluster
            AddNavCluster(keys, ref index, clusterX, row1, row2, unit, gap);

            // Arrow cluster
            AddArrowCluster(keys, ref index, clusterX, row4, row5, unit, gap);

            // Numpad
            var navWidth = 3 * unit + 2 * gap;
            var numpadX = clusterX + navWidth + sectionGap;
            var numpadY = row1;
            AddNumpad(keys, ref index, numpadX, numpadY, unit, gap);

            return keys.ToArray();
        }

        private KeyDefinition[] GenerateTKLKeyboard(double unit, double gap)
        {
            var keys = new List<KeyDefinition>();
            int index = 0;

            AddMainBlock(keys, ref index, unit, gap, includeFunctionRow: true);

            var rowStep = unit + gap;
            var row0 = 0d;
            var row1 = rowStep;
            var row2 = row1 + rowStep;
            var row4 = row2 + rowStep + rowStep;
            var row5 = row4 + rowStep;

            var mainBounds = GetLayoutBounds(keys);
            var sectionGap = unit;
            var clusterX = mainBounds.Width + sectionGap;

            // System keys (top cluster)
            AddKey(keys, ref index, "PrtSc", clusterX, row0, unit, gap);
            AddKey(keys, ref index, "ScrLk", clusterX + (unit + gap), row0, unit, gap);
            AddKey(keys, ref index, "Pause", clusterX + 2 * (unit + gap), row0, unit, gap);

            // Navigation cluster
            AddNavCluster(keys, ref index, clusterX, row1, row2, unit, gap);

            // Arrow cluster
            AddArrowCluster(keys, ref index, clusterX, row4, row5, unit, gap);

            return keys.ToArray();
        }

        private KeyDefinition[] Generate60PercentKeyboard(double unit, double gap)
        {
            var keys = new List<KeyDefinition>();
            int index = 0;

            AddMainBlock(keys, ref index, unit, gap, includeFunctionRow: false);

            return keys.ToArray();
        }

        private static void AddMainBlock(List<KeyDefinition> keys, ref int index, double unit, double gap, bool includeFunctionRow)
        {
            var rowStep = unit + gap;
            var y = 0d;

            if (includeFunctionRow)
            {
                AddFunctionRow(keys, ref index, unit, gap, y);
                y += rowStep;
            }

            AddNumberRow(keys, ref index, unit, gap, y, includeFunctionRow);
            y += rowStep;

            AddTabRow(keys, ref index, unit, gap, y);
            y += rowStep;

            AddCapsRow(keys, ref index, unit, gap, y);
            y += rowStep;

            AddShiftRow(keys, ref index, unit, gap, y);
            y += rowStep;

            AddBottomRow(keys, ref index, unit, gap, y);
        }

        private static void AddFunctionRow(List<KeyDefinition> keys, ref int index, double unit, double gap, double y)
        {
            var x = 0d;
            AddKey(keys, ref index, "Esc", x, y, unit, gap);
            x += unit + gap * 2;

            for (int i = 1; i <= 12; i++)
            {
                if (i == 5 || i == 9)
                {
                    x += gap * 2;
                }

                AddKey(keys, ref index, $"F{i}", x, y, unit, gap);
                x += unit + gap;
            }
        }

        private static void AddNumberRow(List<KeyDefinition> keys, ref int index, double unit, double gap, double y, bool includeFunctionRow)
        {
            var x = 0d;
            if (includeFunctionRow)
            {
                AddKey(keys, ref index, "`", x, y, unit, gap);
            }
            else
            {
                AddKey(keys, ref index, "Esc", x, y, unit, gap);
            }
            x += unit + gap;

            string[] row = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };
            foreach (var key in row)
            {
                AddKey(keys, ref index, key, x, y, unit, gap);
                x += unit + gap;
            }

            AddKey(keys, ref index, "Backspace", x, y, unit, gap, widthUnits: 2);
        }

        private static void AddTabRow(List<KeyDefinition> keys, ref int index, double unit, double gap, double y)
        {
            var x = 0d;
            AddKey(keys, ref index, "Tab", x, y, unit, gap, widthUnits: 1.5);
            x += 1.5 * unit + gap;

            string[] row = { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]", "\\" };
            foreach (var key in row)
            {
                AddKey(keys, ref index, key, x, y, unit, gap);
                x += unit + gap;
            }
        }

        private static void AddCapsRow(List<KeyDefinition> keys, ref int index, double unit, double gap, double y)
        {
            var x = 0d;
            AddKey(keys, ref index, "Caps", x, y, unit, gap, widthUnits: 1.75);
            x += 1.75 * unit + gap;

            string[] row = { "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'" };
            foreach (var key in row)
            {
                AddKey(keys, ref index, key, x, y, unit, gap);
                x += unit + gap;
            }

            AddKey(keys, ref index, "Enter", x, y, unit, gap, widthUnits: 2.25);
        }

        private static void AddShiftRow(List<KeyDefinition> keys, ref int index, double unit, double gap, double y)
        {
            var x = 0d;
            AddKey(keys, ref index, "LShift", x, y, unit, gap, widthUnits: 2.25);
            x += 2.25 * unit + gap;

            string[] row = { "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/" };
            foreach (var key in row)
            {
                AddKey(keys, ref index, key, x, y, unit, gap);
                x += unit + gap;
            }

            AddKey(keys, ref index, "RShift", x, y, unit, gap, widthUnits: 2.75);
        }

        private static void AddBottomRow(List<KeyDefinition> keys, ref int index, double unit, double gap, double y)
        {
            var x = 0d;
            AddKey(keys, ref index, "LCtrl", x, y, unit, gap, widthUnits: 1.25);
            x += 1.25 * unit + gap;
            AddKey(keys, ref index, "LWin", x, y, unit, gap, widthUnits: 1.25);
            x += 1.25 * unit + gap;
            AddKey(keys, ref index, "LAlt", x, y, unit, gap, widthUnits: 1.25);
            x += 1.25 * unit + gap;
            AddKey(keys, ref index, "Space", x, y, unit, gap, widthUnits: 6.25);
            x += 6.25 * unit + gap;
            AddKey(keys, ref index, "RAlt", x, y, unit, gap, widthUnits: 1.25);
            x += 1.25 * unit + gap;
            AddKey(keys, ref index, "RWin", x, y, unit, gap, widthUnits: 1.25);
            x += 1.25 * unit + gap;
            AddKey(keys, ref index, "Menu", x, y, unit, gap, widthUnits: 1.25);
            x += 1.25 * unit + gap;
            AddKey(keys, ref index, "RCtrl", x, y, unit, gap, widthUnits: 1.25);
        }

        private static void AddNavCluster(List<KeyDefinition> keys, ref int index, double x, double yTop, double yBottom, double unit, double gap)
        {
            string[] top = { "Insert", "Home", "PgUp" };
            string[] bottom = { "Delete", "End", "PgDn" };

            for (int i = 0; i < top.Length; i++)
            {
                AddKey(keys, ref index, top[i], x + i * (unit + gap), yTop, unit, gap);
            }

            for (int i = 0; i < bottom.Length; i++)
            {
                AddKey(keys, ref index, bottom[i], x + i * (unit + gap), yBottom, unit, gap);
            }
        }

        private static void AddArrowCluster(List<KeyDefinition> keys, ref int index, double x, double yTop, double yBottom, double unit, double gap)
        {
            AddKey(keys, ref index, "Up", x + (unit + gap), yTop, unit, gap);
            AddKey(keys, ref index, "Left", x, yBottom, unit, gap);
            AddKey(keys, ref index, "Down", x + (unit + gap), yBottom, unit, gap);
            AddKey(keys, ref index, "Right", x + 2 * (unit + gap), yBottom, unit, gap);
        }

        private static void AddNumpad(List<KeyDefinition> keys, ref int index, double x, double y, double unit, double gap)
        {
            string[] numpad = { "NumLock", "Num/", "Num*", "Num-",
                               "Num7", "Num8", "Num9", "Num+",
                               "Num4", "Num5", "Num6",
                               "Num1", "Num2", "Num3", "NumEnter",
                               "Num0", "NumDel" };
            int[] numpadX = { 0, 1, 2, 3,  0, 1, 2, 3,  0, 1, 2,  0, 1, 2, 3,  0, 2 };
            int[] numpadY = { 0, 0, 0, 0,  1, 1, 1, 1,  2, 2, 2,  3, 3, 3, 3,  4, 4 };
            double[] numpadW = { 1, 1, 1, 1,  1, 1, 1, 1,  1, 1, 1,  1, 1, 1, 1,  2, 1 };
            double[] numpadH = { 1, 1, 1, 1,  1, 1, 1, 2,  1, 1, 1,  1, 1, 1, 2,  1, 1 };

            for (int i = 0; i < numpad.Length; i++)
            {
                var posX = x + numpadX[i] * (unit + gap);
                var posY = y + numpadY[i] * (unit + gap);
                AddKey(keys, ref index, numpad[i], posX, posY, unit, gap, numpadW[i], numpadH[i]);
            }
        }

        private static void AddKey(List<KeyDefinition> keys, ref int index, string id, double x, double y, double unit, double gap, double widthUnits = 1, double heightUnits = 1)
        {
            var width = widthUnits * unit + Math.Max(0, widthUnits - 1) * gap;
            var height = heightUnits * unit + Math.Max(0, heightUnits - 1) * gap;
            keys.Add(new KeyDefinition
            {
                Id = id,
                Index = index++,
                X = x,
                Y = y,
                Width = width,
                Height = height
            });
        }

        private static Color ParseColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Colors.Transparent;
            }

            var text = value.Trim();
            if (!text.StartsWith("#", StringComparison.Ordinal))
            {
                text = "#" + text;
            }

            if (Color.TryParse(text, out var parsed))
            {
                return parsed;
            }

            return Colors.Transparent;
        }

        private static bool TryParseColor(string value, out Color color)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                color = Colors.Transparent;
                return false;
            }

            var text = value.Trim();
            if (!text.StartsWith("#", StringComparison.Ordinal))
            {
                text = "#" + text;
            }

            return Color.TryParse(text, out color);
        }

        private void UpdateColorFromRgbText()
        {
            if (_suppressColorTextUpdate)
            {
                return;
            }

            if (!byte.TryParse(SelectedColorR, out var r))
            {
                return;
            }

            if (!byte.TryParse(SelectedColorG, out var g))
            {
                return;
            }

            if (!byte.TryParse(SelectedColorB, out var b))
            {
                return;
            }

            _suppressColorTextUpdate = true;
            SelectedColor = Color.FromRgb(r, g, b);
            _suppressColorTextUpdate = false;
        }

        private KeyboardLayout BuildLayoutFromKeys()
        {
            var keys = KeyButtons
                .Select(button => new KeyDefinition
                {
                    Id = button.Id,
                    Index = button.Index,
                    X = button.X,
                    Y = button.Y,
                    Width = button.Width,
                    Height = button.Height
                })
                .ToArray();

            return new KeyboardLayout
            {
                Width = KeyboardCanvasWidth,
                Height = KeyboardCanvasHeight,
                Keys = keys
            };
        }

        private void InitializeDevices()
        {
            var devices = _preferences.Devices?.ToList() ?? new();
            if (devices.Count == 0)
            {
                var fallback = new DeviceConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = _preferences.DeviceName ?? "Cherry MX Board",
                    ImagePath = _preferences.KeyboardImagePath,
                    LayoutPath = _preferences.KeyboardLayoutPath
                };
                devices.Add(fallback);
                _preferences.Devices = devices.ToArray();
                _preferences.SelectedDeviceId = fallback.Id;
                _preferences.Save();
            }

            Devices = new ObservableCollection<DeviceItemViewModel>(
                devices.Select(cfg => new DeviceItemViewModel(cfg)));

            var selected = Devices.FirstOrDefault(d => d.Id == _preferences.SelectedDeviceId)
                           ?? Devices.FirstOrDefault();
            SelectedDevice = selected;
        }

        private void ApplySelectedDevice()
        {
            if (SelectedDevice == null)
            {
                return;
            }

            _activeDeviceId = SelectedDevice.Id;
            DeviceName = SelectedDevice.Name;
            _preferences.SelectedDeviceId = SelectedDevice.Id;
            SaveDevicesToPreferences();

            KeyboardImage = null;
            KeyboardImagePath = string.Empty;
            _keyboardLayout = null;
            KeyButtons.Clear();
            IsLayoutReady = false;

            if (!string.IsNullOrWhiteSpace(SelectedDevice.ImagePath) && File.Exists(SelectedDevice.ImagePath))
            {
                SetKeyboardImage(SelectedDevice.ImagePath);
            }

            if (!string.IsNullOrWhiteSpace(SelectedDevice.LayoutPath) && File.Exists(SelectedDevice.LayoutPath))
            {
                LoadKeyboardLayout(SelectedDevice.LayoutPath);
            }
        }

        private void AddDevice()
        {
            var config = new DeviceConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = $"Device {Devices.Count + 1}"
            };

            var item = new DeviceItemViewModel(config);
            Devices.Add(item);
            _preferences.SelectedDeviceId = item.Id;
            SaveDevicesToPreferences();
            SelectedDevice = item;
        }

        private void RemoveDevice()
        {
            if (SelectedDevice == null || Devices.Count <= 1)
            {
                return;
            }

            var index = Devices.IndexOf(SelectedDevice);
            Devices.Remove(SelectedDevice);
            _preferences.SelectedDeviceId = Devices.First().Id;
            SaveDevicesToPreferences();
            SelectedDevice = Devices[Math.Clamp(index - 1, 0, Devices.Count - 1)];
        }

        private void SaveDevicesToPreferences()
        {
            _preferences.Devices = Devices.Select(d => d.ToConfig()).ToArray();
            _preferences.Save();
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
