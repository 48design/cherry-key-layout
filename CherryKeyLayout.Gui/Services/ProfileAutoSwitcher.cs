using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CherryKeyLayout;

namespace CherryKeyLayout.Gui.Services
{
    internal sealed class ProfileAutoSwitcher : IDisposable
    {
        private readonly Func<string?> _activeAppProvider;
        private readonly Func<int, Task> _applyProfileAsync;
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(1);
        private readonly SemaphoreSlim _gate = new(1, 1);
        private Timer? _timer;
        private CherryProfileInfo[] _profiles = Array.Empty<CherryProfileInfo>();
        private int _defaultProfileIndex;
        private int _currentProfileIndex = -1;
        private string? _lastActiveAppPath;

        public ProfileAutoSwitcher(Func<string?> activeAppProvider, Func<int, Task> applyProfileAsync)
        {
            _activeAppProvider = activeAppProvider ?? throw new ArgumentNullException(nameof(activeAppProvider));
            _applyProfileAsync = applyProfileAsync ?? throw new ArgumentNullException(nameof(applyProfileAsync));
        }

        public event Action<int, string?>? ActiveProfileChanged;

        public void UpdateProfiles(CherryProfileInfo[] profiles, int defaultProfileIndex)
        {
            _profiles = profiles ?? Array.Empty<CherryProfileInfo>();
            _defaultProfileIndex = defaultProfileIndex;
        }

        public void Start()
        {
            if (_timer != null)
            {
                return;
            }

            _timer = new Timer(async _ => await TickAsync(), null, _interval, _interval);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
            _currentProfileIndex = -1;
        }

        public void Dispose()
        {
            Stop();
            _gate.Dispose();
        }

        private async Task TickAsync()
        {
            if (!await _gate.WaitAsync(0))
            {
                return;
            }

            try
            {
                if (_profiles.Length == 0)
                {
                    return;
                }

                var activePath = NormalizePath(_activeAppProvider());
                var matchIndex = FindProfileForApp(activePath);
                if (matchIndex < 0)
                {
                    matchIndex = _defaultProfileIndex;
                }

                if (matchIndex == _currentProfileIndex)
                {
                    if (!string.Equals(_lastActiveAppPath, activePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _lastActiveAppPath = activePath;
                        ActiveProfileChanged?.Invoke(matchIndex, activePath);
                    }
                    return;
                }

                _currentProfileIndex = matchIndex;
                _lastActiveAppPath = activePath;
                await _applyProfileAsync(matchIndex);
                ActiveProfileChanged?.Invoke(matchIndex, activePath);
            }
            catch
            {
                _currentProfileIndex = -1;
            }
            finally
            {
                _gate.Release();
            }
        }

        private int FindProfileForApp(string? activePath)
        {
            if (string.IsNullOrWhiteSpace(activePath))
            {
                return -1;
            }

            for (var i = 0; i < _profiles.Length; i++)
            {
                var profile = _profiles[i];
                if (!profile.AppEnabled || profile.AppPaths.Length == 0)
                {
                    continue;
                }

                if (profile.AppPaths.Any(path => PathMatches(activePath, path)))
                {
                    return profile.Index;
                }
            }

            return -1;
        }

        private static string? NormalizePath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = value.Trim().Replace('\\', '/');
            if (IsFileNameOnly(normalized))
            {
                return Path.GetFileName(normalized);
            }

            try
            {
                normalized = Path.GetFullPath(normalized).Replace('\\', '/');
            }
            catch
            {
                return normalized;
            }

            return normalized;
        }

        private static bool PathMatches(string activePath, string candidate)
        {
            if (IsFileNameOnly(candidate))
            {
                return string.Equals(
                    Path.GetFileName(activePath),
                    Path.GetFileName(candidate),
                    StringComparison.OrdinalIgnoreCase);
            }

            var normalizedCandidate = NormalizePath(candidate);
            if (string.IsNullOrWhiteSpace(normalizedCandidate))
            {
                return false;
            }

            if (string.Equals(activePath, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (activePath.EndsWith("/" + Path.GetFileName(normalizedCandidate), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(
                Path.GetFileName(activePath),
                Path.GetFileName(normalizedCandidate),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFileNameOnly(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return !value.Contains('/')
                   && !value.Contains('\\')
                   && !Path.IsPathRooted(value);
        }
    }
}
