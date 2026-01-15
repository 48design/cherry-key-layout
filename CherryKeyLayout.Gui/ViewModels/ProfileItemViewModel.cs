using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using CherryKeyLayout.Gui.Services;

namespace CherryKeyLayout.Gui.ViewModels
{
    public sealed class ProfileItemViewModel : INotifyPropertyChanged
    {
        private bool _isDefault;
        private string _title;
        private AvaloniaBitmap? _profileImage;
        private string? _pictureDataUri;
        private string? _pictureSource;

        public ProfileItemViewModel(
            int index,
            string? title,
            bool appEnabled,
            IReadOnlyList<string> appPaths,
            bool isDefault,
            string? pictureDataUri,
            string? pictureSource)
        {
            Index = index;
            _title = string.IsNullOrWhiteSpace(title) ? $"Profile {index + 1}" : title!;
            AppEnabled = appEnabled;
            AppPaths = appPaths?.ToArray() ?? Array.Empty<string>();
            _isDefault = isDefault;
            AppSummary = appEnabled ? $"{AppPaths.Length} linked app(s)" : string.Empty;
            _pictureDataUri = pictureDataUri;
            _pictureSource = pictureSource;
            _profileImage = ProfileImageHelper.TryDecodeDataUri(pictureDataUri);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Index { get; }
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }
        public bool AppEnabled { get; }
        public string[] AppPaths { get; }
        public string AppSummary { get; }
        public AvaloniaBitmap? ProfileImage
        {
            get => _profileImage;
            private set => SetProperty(ref _profileImage, value, nameof(ProfileImage));
        }

        public bool HasProfileImage => ProfileImage != null;

        public string? PictureDataUri => _pictureDataUri;
        public string? PictureSource => _pictureSource;

        public bool IsDefault
        {
            get => _isDefault;
            set => SetProperty(ref _isDefault, value);
        }

        public string DefaultLabel => IsDefault ? "Default" : string.Empty;

        public void UpdatePicture(string? dataUri, string? source)
        {
            _pictureDataUri = dataUri;
            _pictureSource = source;
            ProfileImage = ProfileImageHelper.TryDecodeDataUri(dataUri);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasProfileImage)));
        }

        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            if (propertyName == nameof(IsDefault))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DefaultLabel)));
            }
            else if (propertyName == nameof(ProfileImage))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasProfileImage)));
            }
        }

    }
}
