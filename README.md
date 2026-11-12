# Flake for Windows

A lightweight desktop snowfall overlay for Windows 10 and Windows 11, built with C#, WPF, and .NET 10.

## Current version

- **v1.0** — first working desktop snowfall prototype. The tag preserves the baseline that was tested on the user's Windows computer.
- **Weather controls (development)** — adds configurable wind, flake size, falling speed, and three weather presets.
- **Snow accumulation (development)** — adds a growing, uneven snow layer along the bottom of the screen and a tray command to clear it.

## Features

- Transparent overlay spanning the Windows virtual desktop.
- Animated snow particles with varied size, speed, drift, and opacity.
- Mouse clicks pass through the overlay everywhere except directly over interactive snow objects.
- Notification-area (system tray) menu to pause/resume, select a weather preset, open settings, or exit.
- Snow accumulates along the bottom edge as flakes land, creating a gently uneven surface. The layer is drawn as part of the transparent overlay and does not intercept mouse clicks.
- System-tray command to clear all accumulated snow without restarting the app.
- Settings window with Weather, Guide, and Language tabs. The interface is available in English (default), Russian, and Ukrainian.
- Animated snow cleanup, a configurable clear desktop zone, and an on/off wind toggle with changing gusts.
- Optional cursor interaction: nearby flakes scatter away from the pointer; Mitten mode lets you sweep accumulated snow by moving the pointer across it, without blocking desktop clicks.
- Snowballs can be created from the system-tray menu, dragged around the desktop, rolled across accumulated snow, and grown as they collect it.
- Snowballs collide with each other using a lightweight circle-physics model. Stack three balls from largest at the bottom to smallest at the top to assemble a snowman.
- Mouse input is enabled only over snowballs and snowmen (and while dragging); the rest of the overlay remains click-through.
- Weather settings with:
  - Light snow, normal snow, and blizzard presets.
  - Wind direction (left-to-right or right-to-left).
  - Adjustable wind speed.
  - Adjustable snowflake size.
  - Adjustable falling speed.
- No administrator privileges required.

Weather, language, and cursor-interaction settings currently apply immediately but are not persisted after the application exits. The snow layer is kept in memory and resets when Flake exits. The configurable clear desktop zone limits the general snow layer; future independent snowdrifts will have their own bounds.

## Requirements

- Windows 10 or Windows 11.
- .NET 10 SDK.
- Visual Studio 2026 with the **.NET desktop development** workload, or the .NET CLI.

## Run

From the repository root:

```powershell
dotnet restore .\Flake.sln
dotnet build .\Flake.sln -c Release
dotnet run --project .\src\Flake.App\Flake.App.csproj
```

Find the Flake icon in the notification area (near the clock) to pause snowfall, select a weather preset, open weather settings, or exit.

## Publish a self-contained build

```powershell
dotnet publish .\src\Flake.App\Flake.App.csproj -c Release -r win-x64 --self-contained true
```

The output will be under `src\Flake.App\bin\Release\net10.0-windows\win-x64\publish\`.

## Publish to GitHub

The GitHub repository is the public source location: [Kreyzifrukt/Snow_fall_windows](https://github.com/Kreyzifrukt/Snow_fall_windows). The full project archive supplied during development includes the local commit history and annotated `v1.0` tag. To replace the initial API-import history on GitHub with that complete local history, run `PUBLISH-TO-GITHUB.ps1` from the full archive after `gh auth login`. The script checks that the remote still has the expected import history and requires you to type `REPLACE` before using a lease-protected force push.

## Development stages

The full technical task, requirements, acceptance criteria, and current status are documented in [docs/TECHNICAL-TASK.md](docs/TECHNICAL-TASK.md).

0. **Foundation** — transparent desktop overlay, snow particle rendering, click-through behavior, system tray, and Windows CI.
1. **Weather controls** — wind direction and speed, snowflake size and falling speed, and three weather presets. Implemented; manual Windows verification is pending.
2. **Snow accumulation** — growing uneven snow layer and a tray command to clear it. Implemented; manual Windows verification is pending.
3. **Snowballs and snowmen** — rolling and draggable snowballs, growth from accumulated snow, ball-to-ball collisions, and automatic snowman assembly from a stable three-ball stack. Implemented in the feature branch; CI and manual Windows verification are pending.
4. **Persistence and setup** — save settings/snow state, startup option, and installer. Not started.
5. **Stabilization** — performance, multi-monitor/DPI, and Windows 10/11 compatibility testing. Not started.

## Project structure

```text
Flake-Windows/
├── .github/workflows/build.yml
├── docs/
│   ├── ARCHITECTURE.md
│   └── TECHNICAL-TASK.md
├── src/Flake.App/
│   ├── Interop/
│   ├── Rendering/
│   │   ├── SnowField.cs
│   │   ├── Snowflake.cs
│   │   ├── SnowSurface.cs
│   │   ├── Snowball.cs
│   │   ├── Snowman.cs
│   │   └── WeatherSettings.cs
│   ├── App.xaml
│   ├── App.xaml.cs
│   ├── MainWindow.cs
│   ├── SettingsWindow.cs
│   └── Flake.App.csproj
├── .editorconfig
├── .gitignore
├── Flake.sln
└── README.md
```
