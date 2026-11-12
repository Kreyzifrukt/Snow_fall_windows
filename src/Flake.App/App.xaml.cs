using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using Flake.Rendering;

namespace Flake;

public partial class App : Application
{
    private MainWindow? _overlay;
    private NotifyIcon? _trayIcon;
    private ToolStripMenuItem? _pauseMenuItem;
    private SettingsWindow? _settingsWindow;
    private WeatherSettings _weatherSettings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _overlay = new MainWindow();
        _overlay.ApplyWeatherSettings(_weatherSettings);
        _overlay.Show();
        InitializeTrayIcon();
    }

    private void InitializeTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Flake is running", null, (_, _) => { }).Enabled = false;
        menu.Items.Add(new ToolStripSeparator());

        _pauseMenuItem = new ToolStripMenuItem("Pause snowfall", null, (_, _) => TogglePause());
        menu.Items.Add(_pauseMenuItem);
        menu.Items.Add("Clear accumulated snow", null, (_, _) => _overlay?.ClearAccumulatedSnow());
        menu.Items.Add("Weather settings...", null, (_, _) => ShowSettings());

        var weatherMenu = new ToolStripMenuItem("Weather mode");
        AddWeatherModeOption(weatherMenu, "Light snow", WeatherMode.LightSnow);
        AddWeatherModeOption(weatherMenu, "Normal snow", WeatherMode.NormalSnow);
        AddWeatherModeOption(weatherMenu, "Blizzard", WeatherMode.Blizzard);
        menu.Items.Add(weatherMenu);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit Flake", null, (_, _) => ExitApplication());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Flake - desktop snowfall",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => TogglePause();
    }

    private void AddWeatherModeOption(ToolStripMenuItem parent, string label, WeatherMode mode)
    {
        parent.DropDownItems.Add(label, null, (_, _) => ApplyWeatherSettings(WeatherSettings.ForMode(mode)));
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_weatherSettings, ApplyWeatherSettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ApplyWeatherSettings(WeatherSettings settings)
    {
        _weatherSettings = settings;
        _overlay?.ApplyWeatherSettings(settings);
        _settingsWindow?.UpdateSettings(settings);
    }

    private void TogglePause()
    {
        if (_overlay is null || _pauseMenuItem is null)
        {
            return;
        }

        var isPaused = _overlay.TogglePause();
        _pauseMenuItem.Text = isPaused ? "Resume snowfall" : "Pause snowfall";
    }

    private void ExitApplication()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _settingsWindow?.Close();
        _overlay?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
