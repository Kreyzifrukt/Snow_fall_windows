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
- `CursorInput` reads the global pointer position and physical left-button state through Win32 without capturing mouse input or changing the overlay hit-testing behavior.

## Weather simulation

The simulation advances particles using elapsed frame time rather than assuming a fixed frame rate. Wind adds a horizontal velocity in the selected direction. Falling speed and flake size are multipliers applied to each particle's base values. Weather presets adjust particle count and the three physical settings together; individual controls can then be fine-tuned. When a falling particle reaches the current snow surface, it deposits a small Gaussian-shaped amount of snow around its landing point and respawns above the screen. The resulting height profile is rendered as a single cached WPF geometry behind the falling particles. The profile is bounded to prevent the snow from covering most of the desktop. The tray menu can clear the profile without restarting the application.

Cursor interaction is processed in the existing animation loop. Scatter mode applies a short-lived radial impulse to nearby particles. Mitten mode disables that repulsion, draws a small cursor marker, and polls the physical left-button state globally. While the button is held over accumulated snow, the pointer path removes brush-shaped portions of the snow surface and adds the collected amount to a temporary forming snowball that follows the pointer. Releasing the button drops the formed ball into the simulation. This polling approach does not capture the mouse, so desktop clicks continue to pass through the overlay.

The overlay remains a single WPF visual rather than creating one native window per particle or snow object. Hand-formed snowballs share the existing render loop. Their motion uses frame time, gravity, surface slope, rolling friction, screen-boundary response, and mass-weighted circle collisions. A ball gains additional radius when rolled across an existing snow layer. Three stable balls stacked in decreasing size are snapped into a snowman; the bottom ball remains the physical base and the other two stay aligned to it. No snowball is spawned from a menu command: every ball starts as snow collected from the visible surface.

Mouse interaction uses two paths. Snow gathering is handled through global pointer/button polling and never captures input. Dragging completed snowballs or snowmen uses WPF hit testing and mouse capture; the native click-through style is disabled only over those objects or during an active drag. Elsewhere the overlay continues to pass clicks to desktop applications.

## Current limitations

- Weather settings are held in memory and reset when the application exits.
- Wind is horizontal only.
- Accumulation is limited to the bottom edge of the desktop overlay; snow does not settle on individual application windows or model their shapes.
- The snow layer is not persisted after the application exits.
- Manual snow gathering, snowball formation, drag input, and snowman collision stability are implemented in the development branch but still require manual Windows verification.
- Interaction with individual application windows is not implemented.
- Multi-monitor and mixed-DPI behavior still requires broader testing.
- Cursor position conversion, global button polling, and the mitten gathering brush need manual verification on multi-monitor and mixed-DPI desktops.

## User-facing maintenance improvements

- `Localization` provides English (default), Russian, and Ukrainian strings for the settings window, help tab, and notification-area menu. `AppLanguage` is part of the in-memory settings snapshot; language changes are applied immediately and are not persisted yet.
- `SettingsWindow` contains Weather, Guide, and Language tabs. The guide is localized with the selected language.
- `WeatherSettings.ClearZonePercent` defines the portion of screen height that remains clear above the general snow layer. The surface converts it into a maximum accumulation depth. Future independent snowdrift objects should own separate bounds rather than reusing this general-layer limit.
- `SnowSurface.BeginAnimatedClear` and `UpdateClearAnimation` gradually scale the current snow profile to zero. New deposits are ignored while the clearing animation is active, avoiding a race between the broom animation and falling particles.
- Wind can be disabled. When enabled, its horizontal velocity varies gently over time to simulate gusts; the small per-particle drift remains when wind is disabled.
- Cursor interaction can be disabled independently. Scatter mode repels nearby particles; Mitten mode draws a mitten marker and lets the user hold the left mouse button while moving across accumulated snow to form a ball. The overlay remains click-through because pointer and button state are read globally rather than captured by the window.
