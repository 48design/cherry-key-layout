# cherry-key-layout
Custom C# HID controller for the CHERRY MX Board 3.0S RGB. Reverse-engineered USB protocol inspired by cherryrgb-rs. Provides clean, lightweight RGB control (static color, brightness, effects) without the official CHERRY Utility.

## Quick start

```powershell
dotnet run --project .\\CherryKeyLayout -- --mode static --color #00FFAA --brightness full
dotnet run --project .\\CherryKeyLayout -- --mode wave --color #FF00FF --brightness high --speed slow
```

## Animation examples

```powershell
dotnet run --project .\\CherryKeyLayout -- --mode breathing --color #00A0FF --brightness medium --speed medium
dotnet run --project .\\CherryKeyLayout -- --mode rolling --color #FFAA00 --brightness high --speed slow
dotnet run --project .\\CherryKeyLayout -- --mode spectrum --brightness full --speed fast
```

## Batch files

- `test-quickstart.bat` runs the static color example.
- `test-random-color.bat` picks a random color and applies it.

## Cherry Utility settings

You can load lighting settings from an official `settings.json` and optionally write updated values back:

```powershell
dotnet run --project .\\CherryKeyLayout -- --load-settings .\\settings.json
dotnet run --project .\\CherryKeyLayout -- --mode static --color #FF0000 --save-settings .\\settings.json
```

List profiles stored in a Cherry settings file:

```powershell
dotnet run --project .\\CherryKeyLayout -- --list-profiles .\\settings.json
```

Select a profile by index in the settings file, or apply a different profile without changing the file:

```powershell
dotnet run --project .\\CherryKeyLayout -- --select-profile 1 --load-settings .\\settings.json
dotnet run --project .\\CherryKeyLayout -- --profile-index 2 --load-settings .\\settings.json
```

Custom profiles are supported when `mode` is `Custom` and `customColors` are present.

## Avalonia GUI

Run the GUI app (includes tray icon + auto profile switching):

```powershell
dotnet run --project .\\CherryKeyLayout.Gui
```
