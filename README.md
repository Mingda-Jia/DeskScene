# DeskScene

<img src="assets/icon.svg" width="72" align="right" alt="DeskScene icon">

Save your Windows display **and** audio setup as a "scene", then switch between scenes with one click from the tray.

Typical use: a dual-monitor desk setup, a "TV only" movie setup with HDMI audio, a "desk + TV mirrored" setup — each one click (or one hotkey) away.

## Features

- **Records everything that's in effect now**: which displays are on, resolution, refresh rate, position, orientation, extend / duplicate, main display, HDR, scaling, plus default playback, communications and recording devices and the output volume.
- **Tray icon** — left-click for a quick-switch list, right-click for a menu. The active scene is ticked.
- **Manager window** — save the current setup as a new profile, update a profile from the current setup (with a preview of what changes), rename, delete, and settings.
- **Safe switching** — after a switch you're asked to keep the change; if you don't answer (e.g. the TV shows a black screen), it reverts automatically.
- **Missing devices** — if a profile needs a device that's off or unplugged (like a TV), DeskScene asks: *Retry*, *Apply without it*, or *Cancel* (configurable per profile).
- **Global hotkeys** per profile, **start with Windows**, and optionally **apply a profile at startup**.
- Follows the Windows light/dark theme. No admin rights needed.

## Install

Download `DeskScene.exe` from [Releases](https://github.com/Mingda-Jia/DeskScene/releases) and run it. It's a single self-contained file for Windows 10/11 x64; no installer or .NET runtime needed.

Profiles are stored in `%APPDATA%\DeskScene\profiles.json`.

## Usage

1. Arrange your displays and audio in Windows Settings the way you want.
2. Open DeskScene (tray icon › *Open manager*) and click **Save current as new profile**.
3. Repeat for each setup. Switch from the tray, the manager, or a hotkey.

Recorded settings are read-only in the manager. To change a profile, set Windows up the way you want and click **Update from current**.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet build
dotnet test
dotnet run --project src/DeskScene
```

Publish a single-file exe (self-contained and compressed, about 70 MB):

```powershell
dotnet publish src/DeskScene -c Release -r win-x64 -p:PublishSingleFile=true -o publish
```

## How it works

| Area | Windows API |
|---|---|
| Display topology, modes, primary, extend/duplicate | CCD: `QueryDisplayConfig` / `SetDisplayConfig` (via [CsWin32](https://github.com/microsoft/CsWin32)) |
| HDR | `DisplayConfigSetDeviceInfo` (advanced color state) |
| Scaling | `DisplayConfigGet/SetDeviceInfo` (undocumented DPI-scale request) |
| Default audio devices | Core Audio `IMMDeviceEnumerator` + `IPolicyConfig` (undocumented) |
| Volume | `IAudioEndpointVolume` |
| Hotkeys / start with Windows | `RegisterHotKey` / `HKCU\…\Run` |

Display settings are saved almost exactly as Windows reports them. Monitors and graphics adapters are matched by their stable device paths, so profiles keep working after reboots, driver updates, or plugging a monitor into another port.

Not covered: settings that only exist in the NVIDIA/AMD control panels (color depth, RGB range, G-SYNC, Surround), per-app audio routing, and turning a TV on.

## Project layout

```
src/DeskScene/
  Core/        Profile model, display (CCD) and audio services, store, hotkeys, autostart
  UI/          Tray, quick-switch flyout, manager window, dialogs, layout preview
  Switcher.cs  Apply flow: missing-device check → displays → audio → keep or revert
tests/DeskScene.Tests/   Unit tests for the pure logic (matching, diff, storage, hotkeys)
assets/      icon.svg (source) → icon.ico via make-icon.ps1
```

## License

[MIT](LICENSE)
