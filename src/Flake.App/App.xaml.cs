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
    private ContextMenuStrip? _trayMenu;
    private SettingsWindow? _settingsWindow;
    private WeatherSettings _weatherSettings = new();
    private bool _isPaused;

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
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Flake - desktop snowfall",
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => TogglePause();
        UpdateTrayMenu();
    }

    private void UpdateTrayMenu()
    {
        if (_trayIcon is null)
        {
            return;
        }

        string T(string key) => Localization.Get(_weatherSettings.Language, key);
        _trayIcon.Text = T("trayTooltip");

        var previousMenu = _trayMenu;
        var menu = new ContextMenuStrip();
        menu.Items.Add(T("trayRunning"), null, (_, _) => { }).Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T(_isPaused ? "trayResume" : "trayPause"), null, (_, _) => TogglePause());
        menu.Items.Add(T("trayClear"), null, (_, _) => _overlay?.ClearAccumulatedSnow());
        menu.Items.Add(T("trayClearBalls"), null, (_, _) => _overlay?.ClearSnowballs());
        menu.Items.Add(T("trayClearAllSnow"), null, (_, _) => _overlay?.ClearAllSnow());
        menu.Items.Add(T("traySettings"), null, (_, _) => ShowSettings());

        var weatherMenu = new ToolStripMenuItem(T("trayWeatherMode"));
        AddWeatherModeOption(weatherMenu, T("lightSnow"), WeatherMode.LightSnow);
        AddWeatherModeOption(weatherMenu, T("normalSnow"), WeatherMode.NormalSnow);
        AddWeatherModeOption(weatherMenu, T("blizzard"), WeatherMode.Blizzard);
        menu.Items.Add(weatherMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("trayExit"), null, (_, _) => ExitApplication());

        _trayMenu = menu;
        _trayIcon.ContextMenuStrip = menu;
        previousMenu?.Dispose();
    }

    private void AddWeatherModeOption(ToolStripMenuItem parent, string label, WeatherMode mode)
    {
        parent.DropDownItems.Add(label, null, (_, _) => ApplyWeatherSettings(_weatherSettings.WithModePreservingPreferences(mode)));
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
        var languageChanged = _weatherSettings.Language != settings.Language;
        _weatherSettings = settings;
        _overlay?.ApplyWeatherSettings(settings);
        _settingsWindow?.UpdateSettings(settings, languageChanged);

        if (languageChanged)
        {
            UpdateTrayMenu();
        }
    }

    private void TogglePause()
    {
        if (_overlay is null)
        {
            return;
        }

        _isPaused = _overlay.TogglePause();
        UpdateTrayMenu();
    }

    private void ExitApplication()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _trayMenu?.Dispose();
        _trayMenu = null;
        _settingsWindow?.Close();
        _overlay?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _trayMenu?.Dispose();
        base.OnExit(e);
    }
}
