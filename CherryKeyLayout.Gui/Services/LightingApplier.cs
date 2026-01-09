using System;
using System.Threading;
using System.Threading.Tasks;
using CherryKeyLayout;

namespace CherryKeyLayout.Gui.Services
{
    internal sealed class LightingApplier
    {
        private const ushort CherryVid = 0x046A;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public async Task ApplyProfileAsync(string settingsPath, int profileIndex, bool syncSelectedProfile)
        {
            await _gate.WaitAsync();
            try
            {
                var lighting = CherrySettings.LoadLighting(settingsPath, profileIndex);
                using var keyboard = CherryKeyboard.Open(CherryVid, null);

                var useCustom = lighting.Mode == LightingMode.Custom
                                && lighting.CustomColors != null
                                && lighting.CustomColors.Length > 0;

                if (useCustom)
                {
                    keyboard.SetCustomColors(lighting.CustomColors!, lighting.Brightness, lighting.Speed);
                }
                else if (lighting.Mode == LightingMode.Static)
                {
                    keyboard.SetStaticColor(lighting.Color, lighting.Brightness);
                }
                else
                {
                    keyboard.SetAnimation(lighting.Mode, lighting.Brightness, lighting.Speed, lighting.Color, false);
                }

                if (syncSelectedProfile)
                {
                    CherrySettings.SetSelectedProfile(settingsPath, profileIndex);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task ApplyLightingAsync(
            string settingsPath,
            int profileIndex,
            LightingMode mode,
            Rgb color,
            Brightness brightness,
            Speed speed,
            bool rainbow,
            bool syncSelectedProfile)
        {
            await _gate.WaitAsync();
            try
            {
                using var keyboard = CherryKeyboard.Open(CherryVid, null);
                if (mode == LightingMode.Static)
                {
                    keyboard.SetStaticColor(color, brightness);
                }
                else
                {
                    keyboard.SetAnimation(mode, brightness, speed, color, rainbow);
                }

                if (syncSelectedProfile)
                {
                    var lighting = new CherrySettingsLighting
                    {
                        Mode = mode,
                        Brightness = brightness,
                        Speed = speed,
                        Color = color
                    };
                    CherrySettings.SaveLighting(settingsPath, lighting, profileIndex);
                    CherrySettings.SetSelectedProfile(settingsPath, profileIndex);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task ApplyCustomColorsAsync(
            string settingsPath,
            int profileIndex,
            Rgb[] colors,
            Brightness brightness,
            Speed speed,
            bool syncSelectedProfile)
        {
            await _gate.WaitAsync();
            try
            {
                using var keyboard = CherryKeyboard.Open(CherryVid, null);
                keyboard.SetCustomColors(colors, brightness, speed);

                if (syncSelectedProfile)
                {
                    var lighting = CherrySettings.LoadLighting(settingsPath, profileIndex);
                    lighting.Mode = LightingMode.Custom;
                    lighting.Brightness = brightness;
                    lighting.Speed = speed;
                    lighting.CustomColors = colors;
                    CherrySettings.SaveLighting(settingsPath, lighting, profileIndex);
                    CherrySettings.SetSelectedProfile(settingsPath, profileIndex);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
