# Architecture

## Current components

- `App` owns application lifetime, the notification-area icon, current weather settings, and the settings window.
- `MainWindow` is the transparent, click-through desktop overlay.
- `SnowField` owns the particle collection, animation loop, collision checks, and WPF drawing.
- `SnowSurface` stores the snow-height profile, adds snow where particles land, preserves the profile during overlay resizing, and caches the snow-layer geometry.
- `Snowflake` stores per-particle position and visual/physical parameters.
- `WeatherSettings` is an immutable settings snapshot. Applying a new snapshot updates the simulation without rebuilding every particle unless the selected preset changes particle count.
- `SettingsWindow` edits weather settings and sends updated snapshots to `App`.
- `NativeWindowStyles` contains the Win32 interop needed to make the overlay click-through.

## Weather simulation

The simulation advances particles using elapsed frame time rather than assuming a fixed frame rate. Wind adds a horizontal velocity in the selected direction. Falling speed and flake size are multipliers applied to each particle's base values. Weather presets adjust particle count and the three physical settings together; individual controls can then be fine-tuned. When a falling particle reaches the current snow surface, it deposits a small Gaussian-shaped amount of snow around its landing point and respawns above the screen. The resulting height profile is rendered as a single cached WPF geometry behind the falling particles. The profile is bounded to prevent the snow from covering most of the desktop. The tray menu can clear the profile without restarting the application.

The overlay remains a single WPF visual rather than creating one native window per snowflake. This keeps the initial implementation relatively simple and avoids a large number of operating-system windows.

## Current limitations

- Weather settings are held in memory and reset when the application exits.
- Wind is horizontal only.
- Accumulation is limited to the bottom edge of the desktop overlay; snow does not settle on individual application windows or model their shapes.
- The snow layer is not persisted after the application exits.
- Snowballs, snowmen, and interaction with individual application windows are not implemented.
- Multi-monitor and mixed-DPI behavior still requires broader testing.
