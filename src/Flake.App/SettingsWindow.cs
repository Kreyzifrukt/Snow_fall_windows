using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Flake.Rendering;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfControl = System.Windows.Controls.Control;
using WpfPanel = System.Windows.Controls.Panel;

namespace Flake;

internal sealed class SettingsWindow : Window
{
    private readonly Action<WeatherSettings> _onSettingsChanged;
    private readonly WpfComboBox _modeBox;
    private readonly WpfComboBox _directionBox;
    private readonly Slider _windSpeedSlider;
    private readonly Slider _flakeSizeSlider;
    private readonly Slider _fallSpeedSlider;
    private readonly TextBlock _windSpeedValue;
    private readonly TextBlock _flakeSizeValue;
    private readonly TextBlock _fallSpeedValue;
    private WeatherSettings _settings;
    private bool _isUpdatingControls;

    public SettingsWindow(WeatherSettings settings, Action<WeatherSettings> onSettingsChanged)
    {
        _settings = settings;
        _onSettingsChanged = onSettingsChanged;

        Title = "Flake — Weather settings";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(WpfColor.FromRgb(245, 248, 252));
        FontFamily = new WpfFontFamily("Segoe UI");

        _modeBox = CreateComboBox(["Light snow", "Normal snow", "Blizzard"]);
        _directionBox = CreateComboBox(["Left to right", "Right to left"]);
        _windSpeedSlider = CreateSlider(0, 120);
        _flakeSizeSlider = CreateSlider(0.5, 2.0);
        _fallSpeedSlider = CreateSlider(0.5, 2.0);
        _windSpeedValue = CreateValueText();
        _flakeSizeValue = CreateValueText();
        _fallSpeedValue = CreateValueText();

        var content = new StackPanel { Margin = new Thickness(24), Orientation = WpfOrientation.Vertical };
        content.Children.Add(new TextBlock
        {
            Text = "Weather settings",
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(30, 42, 58)),
            Margin = new Thickness(0, 0, 0, 18)
        });

        AddLabeledControl(content, "Weather mode", _modeBox);
        AddLabeledControl(content, "Wind direction", _directionBox);
        AddSliderControl(content, "Wind speed", _windSpeedSlider, _windSpeedValue, "0", "120");
        AddSliderControl(content, "Snowflake size", _flakeSizeSlider, _flakeSizeValue, "Small", "Large");
        AddSliderControl(content, "Falling speed", _fallSpeedSlider, _fallSpeedValue, "Slow", "Fast");

        var closeButton = new WpfButton
        {
            Content = "Close",
            Width = 100,
            Height = 34,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(12, 4, 12, 4)
        };
        closeButton.Click += (_, _) => Close();
        content.Children.Add(closeButton);

        Content = content;
        SetControlsFromSettings();
        WireEvents();
    }

    public void UpdateSettings(WeatherSettings settings)
    {
        _settings = settings;
        SetControlsFromSettings();
    }

    private void WireEvents()
    {
        _modeBox.SelectionChanged += (_, _) =>
        {
            if (_isUpdatingControls || _modeBox.SelectedIndex < 0)
            {
                return;
            }

            var mode = (WeatherMode)_modeBox.SelectedIndex;
            _settings = WeatherSettings.ForMode(mode) with { WindDirection = _settings.WindDirection };
            SetControlsFromSettings();
            PublishSettings();
        };

        _directionBox.SelectionChanged += (_, _) =>
        {
            if (_isUpdatingControls || _directionBox.SelectedIndex < 0)
            {
                return;
            }

            _settings = _settings with
            {
                WindDirection = _directionBox.SelectedIndex == 0
                    ? WindDirection.LeftToRight
                    : WindDirection.RightToLeft
            };
            PublishSettings();
        };

        _windSpeedSlider.ValueChanged += (_, _) => UpdateFromSliders();
        _flakeSizeSlider.ValueChanged += (_, _) => UpdateFromSliders();
        _fallSpeedSlider.ValueChanged += (_, _) => UpdateFromSliders();
    }

    private void UpdateFromSliders()
    {
        if (_isUpdatingControls)
        {
            return;
        }

        _settings = _settings with
        {
            WindSpeed = _windSpeedSlider.Value,
            FlakeSize = _flakeSizeSlider.Value,
            FallSpeed = _fallSpeedSlider.Value
        };

        _windSpeedValue.Text = $"{_settings.WindSpeed:0}";
        _flakeSizeValue.Text = $"{_settings.FlakeSize:0.0}×";
        _fallSpeedValue.Text = $"{_settings.FallSpeed:0.0}×";
        PublishSettings();
    }

    private void PublishSettings() => _onSettingsChanged(_settings);

    private void SetControlsFromSettings()
    {
        _isUpdatingControls = true;
        _modeBox.SelectedIndex = (int)_settings.Mode;
        _directionBox.SelectedIndex = _settings.WindDirection == WindDirection.LeftToRight ? 0 : 1;
        _windSpeedSlider.Value = _settings.WindSpeed;
        _flakeSizeSlider.Value = _settings.FlakeSize;
        _fallSpeedSlider.Value = _settings.FallSpeed;
        _windSpeedValue.Text = $"{_settings.WindSpeed:0}";
        _flakeSizeValue.Text = $"{_settings.FlakeSize:0.0}×";
        _fallSpeedValue.Text = $"{_settings.FallSpeed:0.0}×";
        _isUpdatingControls = false;
    }

    private static WpfComboBox CreateComboBox(string[] items)
    {
        var comboBox = new WpfComboBox
        {
            ItemsSource = items,
            Height = 30,
            MinWidth = 180,
            HorizontalAlignment = WpfHorizontalAlignment.Stretch
        };
        return comboBox;
    }

    private static Slider CreateSlider(double minimum, double maximum) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        TickFrequency = (maximum - minimum) / 10,
        IsSnapToTickEnabled = false,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static TextBlock CreateValueText() => new()
    {
        Width = 48,
        TextAlignment = TextAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(WpfColor.FromRgb(65, 78, 96))
    };

    private static void AddLabeledControl(WpfPanel parent, string label, WpfControl control)
    {
        parent.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 8, 0, 5),
            Foreground = new SolidColorBrush(WpfColor.FromRgb(55, 68, 86))
        });
        parent.Children.Add(control);
    }

    private static void AddSliderControl(
        WpfPanel parent,
        string label,
        Slider slider,
        TextBlock value,
        string minimumLabel,
        string maximumLabel)
    {
        parent.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 12, 0, 2),
            Foreground = new SolidColorBrush(WpfColor.FromRgb(55, 68, 86))
        });

        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
        Grid.SetColumn(slider, 0);
        Grid.SetColumn(value, 1);
        row.Children.Add(slider);
        row.Children.Add(value);
        parent.Children.Add(row);

        var range = new Grid
        {
            Margin = new Thickness(0, 0, 0, 2),
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() }
        };
        var minText = new TextBlock { Text = minimumLabel, FontSize = 11, Foreground = WpfBrushes.Gray };
        var maxText = new TextBlock { Text = maximumLabel, FontSize = 11, Foreground = WpfBrushes.Gray, HorizontalAlignment = WpfHorizontalAlignment.Right };
        Grid.SetColumn(minText, 0);
        Grid.SetColumn(maxText, 1);
        range.Children.Add(minText);
        range.Children.Add(maxText);
        parent.Children.Add(range);
    }
}
