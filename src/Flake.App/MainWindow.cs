using System.Windows;
using System.Windows.Interop;
using Flake.Interop;
using Flake.Rendering;

namespace Flake;

public sealed class MainWindow : Window
{
    private readonly SnowField _snowField;
    private IntPtr _overlayHandle;

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
        _snowField.ClickThroughChanged += OnClickThroughChanged;
        Content = _snowField;
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => _snowField.Dispose();
    }

    public bool TogglePause() => _snowField.TogglePause();

    internal void ApplyWeatherSettings(WeatherSettings settings) => _snowField.ApplySettings(settings);

    internal void ClearAccumulatedSnow() => _snowField.ClearAccumulatedSnow();

    internal void ClearSnowballs() => _snowField.ClearSnowballs();

    internal void ClearAllSnow() => _snowField.ClearAllSnow();

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _overlayHandle = new WindowInteropHelper(this).Handle;
        NativeWindowStyles.MakeOverlayClickThrough(_overlayHandle);
    }

    private void OnClickThroughChanged(bool clickThrough)
    {
        NativeWindowStyles.SetClickThrough(_overlayHandle, clickThrough);
    }
}
