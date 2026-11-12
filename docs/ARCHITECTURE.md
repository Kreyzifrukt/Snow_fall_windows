# Architecture

## Current components

- `App` owns application lifetime, the notification-area icon, current weather settings, and the settings window.
- `MainWindow` is the transparent, click-through desktop overlay.
- `SnowField` owns the particle collection, animation loop, collision checks, and WPF drawing.
- `SnowSurface` stores the snow-height profile, adds snow where particles land, preserves the profile during overlay resizing, and caches the snow-layer geometry.
- `Snowflake` stores per-particle position and visual/physical parameters.
- `Snowball` stores the position, velocity, size, rotation, and mass used by the interactive rigid-circle simulation.
- `Snowman` groups three existing snowballs, keeps their stack aligned, and draws the face, scarf, and buttons.
- `WeatherSettings` is an immutable settings snapshot. Applying a new snapshot updates the simulation without rebuilding every particle unless the selected preset changes particle count.
- `SettingsWindow` edits weather settings and sends updated snapshots to `App`.
- `NativeWindowStyles` contains the Win32 interop needed to make the overlay click-through.
- `CursorInput` reads the global pointer position through Win32 without changing the overlay hit-testing behavior.

## Weather simulation

The simulation advances particles using elapsed frame time rather than assuming a fixed frame rate. Wind adds a horizontal velocity in the selected direction. Falling speed and flake size are multipliers applied to each particle's base values. Weather presets adjust particle count and the three physical settings together; individual controls can then be fine-tuned. When a falling particle reaches the current snow surface, it deposits a small Gaussian-shaped amount of snow around its landing point and respawns above the screen. The resulting height profile is rendered as a single cached WPF geometry behind the falling particles. The profile is bounded to prevent the snow from covering most of the desktop. The tray menu can clear the profile without restarting the application.

Cursor interaction is processed in the existing animation loop. Nearby particles receive a short-lived radial impulse and gradually lose that velocity through damping. Mitten mode draws a small custom cursor marker and samples the pointer path while the pointer moves across the snow layer; the snow surface removes a soft brush-shaped amount of snow along that path. Global pointer polling is used so the transparent overlay continues to pass clicks to other applications.

The overlay remains a single WPF visual rather than creating one native window per particle or snow object. Snowballs share the existing render loop. Their motion uses frame time, gravity, surface slope, rolling friction, screen-boundary response, and mass-weighted circle collisions. While a ball rolls across an existing snow layer, it removes a thin strip of snow and gains radius from the collected amount. Three stable balls stacked in decreasing size are snapped into a snowman; the bottom ball remains the physical base and the other two stay aligned to it.

Mouse interaction uses the existing global pointer polling plus WPF hit testing. The native click-through style is disabled only when the pointer is over a snowball/snowman or while a drag is active. The visual's hit-test override only returns a target for those circular regions, and mouse capture is used during the drag. Elsewhere the overlay continues to pass clicks to desktop applications.

## Current limitations

- Weather settings are held in memory and reset when the application exits.
- Wind is horizontal only.
- Accumulation is limited to the bottom edge of the desktop overlay; snow does not settle on individual application windows or model their shapes.
- The snow layer is not persisted after the application exits.
- Snowball and snowman code is present in the development branch; visual behavior, drag input, and collision stability still require manual Windows verification.
- Interaction with individual application windows is not implemented.
- Multi-monitor and mixed-DPI behavior still requires broader testing.
- Cursor position conversion and the mitten brush need manual verification on multi-monitor and mixed-DPI desktops.

## User-facing maintenance improvements

- `Localization` provides English (default), Russian, and Ukrainian strings for the settings window, help tab, and notification-area menu. `AppLanguage` is part of the in-memory settings snapshot; language changes are applied immediately and are not persisted yet.
- `SettingsWindow` contains Weather, Guide, and Language tabs. The guide is localized with the selected language.
- `WeatherSettings.ClearZonePercent` defines the portion of screen height that remains clear above the general snow layer. The surface converts it into a maximum accumulation depth. Future independent snowdrift objects should own separate bounds rather than reusing this general-layer limit.
- `SnowSurface.BeginAnimatedClear` and `UpdateClearAnimation` gradually scale the current snow profile to zero. New deposits are ignored while the clearing animation is active, avoiding a race between the broom animation and falling particles.
- Wind can be disabled. When enabled, its horizontal velocity varies gently over time to simulate gusts; the small per-particle drift remains when wind is disabled.
- Cursor interaction can be disabled independently. Scatter mode repels nearby particles; Mitten mode also draws a mitten marker and sweeps the accumulated snow as the pointer moves across it. The overlay remains click-through because pointer input is read globally rather than captured by the window.
