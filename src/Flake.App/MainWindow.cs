using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Flake.Interop;
using Flake.Rendering;

namespace Flake;

public sealed class MainWindow : Window
{
    private readonly SnowField _snowField;
    private readonly DispatcherTimer _fullscreenMonitor;
    private IntPtr _overlayHandle;
    private bool _isHiddenForFullscreen;

    public MainWindow()
    {
        Title = "Flake";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _snowField = new SnowField();
        _fullscreenMonitor = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _fullscreenMonitor.Tick += CheckFullscreenForeground;
        _snowField.ClickThroughChanged += OnClickThroughChanged;
        Content = _snowField;
        SourceInitialized += OnSourceInitialized;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Closed += (_, _) =>
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _fullscreenMonitor.Stop();
            _snowField.Dispose();
        };
    }

    public bool TogglePause() => _snowField.TogglePause();

    public bool ToggleSnowfallGeneration() => _snowField.ToggleSnowfallGeneration();

    internal void ApplyWeatherSettings(WeatherSettings settings) => _snowField.ApplySettings(settings);

    internal void ClearAccumulatedSnow() => _snowField.ClearAccumulatedSnow();

    internal void ClearSnowballs() => _snowField.ClearSnowballs();

    internal void ClearAllSnow() => _snowField.ClearAllSnow();

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _overlayHandle = new WindowInteropHelper(this).Handle;
        NativeWindowStyles.MakeOverlayClickThrough(_overlayHandle);
        _fullscreenMonitor.Start();
    }

    private void OnClickThroughChanged(bool clickThrough)
    {
        NativeWindowStyles.SetClickThrough(_overlayHandle, clickThrough);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(RefreshVirtualScreenBounds));
    }

    private void RefreshVirtualScreenBounds()
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    private void CheckFullscreenForeground(object? sender, EventArgs e)
    {
        var shouldHide = WindowSnowSurface.IsFullscreenForegroundWindow(Environment.ProcessId);
        if (shouldHide == _isHiddenForFullscreen)
        {
            return;
        }

        _isHiddenForFullscreen = shouldHide;
        if (shouldHide)
        {
            Hide();
        }
        else
        {
            Show();
            Topmost = true;
        }
    }
}
