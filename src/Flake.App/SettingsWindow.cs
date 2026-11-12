using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Flake.Rendering;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfColor = System.Windows.Media.Color;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPanel = System.Windows.Controls.Panel;
using WpfTabControl = System.Windows.Controls.TabControl;

namespace Flake;

internal sealed class SettingsWindow : Window
{
    private readonly Action<WeatherSettings> _onSettingsChanged;
    private readonly WpfComboBox _modeBox;
    private readonly WpfComboBox _directionBox;
    private readonly WpfComboBox _languageBox;
    private readonly WpfComboBox _cursorModeBox;
    private readonly WpfCheckBox _windEnabledCheckBox;
    private readonly WpfCheckBox _cursorInteractionCheckBox;
    private readonly Slider _windSpeedSlider;
    private readonly Slider _flakeSizeSlider;
    private readonly Slider _fallSpeedSlider;
    private readonly Slider _clearZoneSlider;
    private readonly TextBlock _windSpeedValue;
    private readonly TextBlock _flakeSizeValue;
    private readonly TextBlock _fallSpeedValue;
    private readonly TextBlock _clearZoneValue;
    private readonly TextBlock _modeLabel;
    private readonly TextBlock _directionLabel;
    private readonly TextBlock _windSpeedLabel;
    private readonly TextBlock _flakeSizeLabel;
    private readonly TextBlock _fallSpeedLabel;
    private readonly TextBlock _clearZoneLabel;
    private readonly TextBlock _clearZoneHint;
    private readonly TextBlock _cursorInteractionLabel;
    private readonly TextBlock _guideText;
    private readonly TextBlock _languageLabel;
    private readonly TextBlock _languageHint;
    private readonly TabItem _weatherTab;
    private readonly TabItem _guideTab;
    private readonly TabItem _languageTab;
    private readonly WpfButton _closeButton;
    private WeatherSettings _settings;
    private bool _isUpdatingControls;

    public SettingsWindow(WeatherSettings settings, Action<WeatherSettings> onSettingsChanged)
    {
        _settings = settings;
        _onSettingsChanged = onSettingsChanged;

        Title = "Flake";
        Width = 520;
        Height = 610;
        MinWidth = 480;
        MinHeight = 560;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(WpfColor.FromRgb(245, 248, 252));
        FontFamily = new WpfFontFamily("Segoe UI");

        _modeBox = CreateComboBox();
        _directionBox = CreateComboBox();
        _languageBox = CreateComboBox();
        _cursorModeBox = CreateComboBox();
        _windEnabledCheckBox = new WpfCheckBox { Margin = new Thickness(0, 10, 0, 4) };
        _cursorInteractionCheckBox = new WpfCheckBox { Margin = new Thickness(0, 14, 0, 4) };
        _windSpeedSlider = CreateSlider(0, 120);
        _flakeSizeSlider = CreateSlider(0.5, 2.0);
        _fallSpeedSlider = CreateSlider(0.5, 2.0);
        _clearZoneSlider = CreateSlider(50, 90);
        _windSpeedValue = CreateValueText();
        _flakeSizeValue = CreateValueText();
        _fallSpeedValue = CreateValueText();
        _clearZoneValue = CreateValueText();

        _modeLabel = CreateLabel();
        _directionLabel = CreateLabel();
        _windSpeedLabel = CreateLabel();
        _flakeSizeLabel = CreateLabel();
        _fallSpeedLabel = CreateLabel();
        _clearZoneLabel = CreateLabel();
        _clearZoneHint = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = WpfBrushes.Gray, Margin = new Thickness(0, 2, 0, 6) };
        _cursorInteractionLabel = CreateLabel();
        _guideText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 23, Foreground = new SolidColorBrush(WpfColor.FromRgb(55, 68, 86)), Margin = new Thickness(4, 8, 4, 8) };
        _languageLabel = CreateLabel();
        _languageHint = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = WpfBrushes.Gray, Margin = new Thickness(0, 8, 0, 0) };

        _weatherTab = new TabItem();
        _guideTab = new TabItem();
        _languageTab = new TabItem();
        _closeButton = new WpfButton
        {
            Width = 110,
            Height = 34,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(12, 4, 12, 4)
        };
        _closeButton.Click += (_, _) => Close();

        var tabs = new WpfTabControl { Margin = new Thickness(18, 18, 18, 8) };
        tabs.Items.Add(_weatherTab);
        tabs.Items.Add(_guideTab);
        tabs.Items.Add(_languageTab);
        _weatherTab.Content = BuildWeatherTab();
        _guideTab.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _guideText, Margin = new Thickness(12) };
        _languageTab.Content = BuildLanguageTab();

        var root = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(_closeButton, Dock.Bottom);
        root.Children.Add(_closeButton);
        root.Children.Add(tabs);
        Content = root;

        ApplyLocalization();
        SetControlsFromSettings();
        WireEvents();
    }

    public void UpdateSettings(WeatherSettings settings, bool languageChanged)
    {
        _settings = settings;
        if (languageChanged)
        {
            ApplyLocalization();
        }
        SetControlsFromSettings();
    }

    private UIElement BuildWeatherTab()
    {
        var content = new StackPanel { Margin = new Thickness(18), Orientation = WpfOrientation.Vertical };
        content.Children.Add(_modeLabel);
        content.Children.Add(_modeBox);
        content.Children.Add(_windEnabledCheckBox);
        content.Children.Add(_directionLabel);
        content.Children.Add(_directionBox);
        content.Children.Add(_windSpeedLabel);
        AddSliderRow(content, _windSpeedSlider, _windSpeedValue, "0", "120");
        content.Children.Add(_flakeSizeLabel);
        AddSliderRow(content, _flakeSizeSlider, _flakeSizeValue, "0.5×", "2×");
        content.Children.Add(_fallSpeedLabel);
        AddSliderRow(content, _fallSpeedSlider, _fallSpeedValue, "0.5×", "2×");
        content.Children.Add(_clearZoneLabel);
        AddSliderRow(content, _clearZoneSlider, _clearZoneValue, "50%", "90%");
        content.Children.Add(_clearZoneHint);
        content.Children.Add(_cursorInteractionCheckBox);
        content.Children.Add(_cursorInteractionLabel);
        content.Children.Add(_cursorModeBox);
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content
        };
    }

    private UIElement BuildLanguageTab()
    {
        var content = new StackPanel { Margin = new Thickness(22), Orientation = WpfOrientation.Vertical };
        content.Children.Add(_languageLabel);
        content.Children.Add(_languageBox);
        content.Children.Add(_languageHint);
        return content;
    }

    private void WireEvents()
    {
        _modeBox.SelectionChanged += (_, _) =>
        {
            if (_isUpdatingControls || _modeBox.SelectedIndex < 0)
            {
                return;
            }

            _settings = _settings.WithModePreservingPreferences((WeatherMode)_modeBox.SelectedIndex);
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

        _languageBox.SelectionChanged += (_, _) =>
        {
            if (_isUpdatingControls || _languageBox.SelectedIndex < 0)
            {
                return;
            }

            _settings = _settings with { Language = (AppLanguage)_languageBox.SelectedIndex };
            ApplyLocalization();
            SetControlsFromSettings();
            PublishSettings();
        };

        _windEnabledCheckBox.Checked += (_, _) => UpdateWindEnabled(true);
        _windEnabledCheckBox.Unchecked += (_, _) => UpdateWindEnabled(false);
        _cursorInteractionCheckBox.Checked += (_, _) => UpdateCursorInteraction(true);
        _cursorInteractionCheckBox.Unchecked += (_, _) => UpdateCursorInteraction(false);
        _cursorModeBox.SelectionChanged += (_, _) =>
        {
            if (_isUpdatingControls || _cursorModeBox.SelectedIndex < 0)
            {
                return;
            }

            _settings = _settings with
            {
                CursorMode = (CursorInteractionMode)_cursorModeBox.SelectedIndex
            };
            PublishSettings();
        };
        _windSpeedSlider.ValueChanged += (_, _) => UpdateFromSliders();
        _flakeSizeSlider.ValueChanged += (_, _) => UpdateFromSliders();
        _fallSpeedSlider.ValueChanged += (_, _) => UpdateFromSliders();
        _clearZoneSlider.ValueChanged += (_, _) => UpdateFromSliders();
    }

    private void UpdateWindEnabled(bool enabled)
    {
        if (_isUpdatingControls)
        {
            return;
        }

        _settings = _settings with { WindEnabled = enabled };
        PublishSettings();
    }

    private void UpdateCursorInteraction(bool enabled)
    {
        if (_isUpdatingControls)
        {
            return;
        }

        _settings = _settings with { CursorInteractionEnabled = enabled };
        _cursorModeBox.IsEnabled = enabled;
        PublishSettings();
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
            FallSpeed = _fallSpeedSlider.Value,
            ClearZonePercent = _clearZoneSlider.Value
        };

        UpdateValueLabels();
        PublishSettings();
    }

    private void PublishSettings() => _onSettingsChanged(_settings);

    private void SetControlsFromSettings()
    {
        _isUpdatingControls = true;
        _modeBox.SelectedIndex = (int)_settings.Mode;
        _directionBox.SelectedIndex = _settings.WindDirection == WindDirection.LeftToRight ? 0 : 1;
        _languageBox.SelectedIndex = (int)_settings.Language;
        _windEnabledCheckBox.IsChecked = _settings.WindEnabled;
        _cursorInteractionCheckBox.IsChecked = _settings.CursorInteractionEnabled;
        _cursorModeBox.SelectedIndex = (int)_settings.CursorMode;
        _cursorModeBox.IsEnabled = _settings.CursorInteractionEnabled;
        _directionBox.IsEnabled = _settings.WindEnabled;
        _windSpeedSlider.IsEnabled = _settings.WindEnabled;
        _windSpeedSlider.Value = _settings.WindSpeed;
        _flakeSizeSlider.Value = _settings.FlakeSize;
        _fallSpeedSlider.Value = _settings.FallSpeed;
        _clearZoneSlider.Value = _settings.ClearZonePercent;
        UpdateValueLabels();
        _isUpdatingControls = false;
    }

    private void UpdateValueLabels()
    {
        _windSpeedValue.Text = $"{_settings.WindSpeed:0}";
        _flakeSizeValue.Text = $"{_settings.FlakeSize:0.0}×";
        _fallSpeedValue.Text = $"{_settings.FallSpeed:0.0}×";
        _clearZoneValue.Text = $"{_settings.ClearZonePercent:0}%";
    }

    private void ApplyLocalization()
    {
        string T(string key) => Localization.Get(_settings.Language, key);

        Title = T("settingsTitle");
        _weatherTab.Header = T("tabWeather");
        _guideTab.Header = T("tabGuide");
        _languageTab.Header = T("tabLanguage");
        _modeLabel.Text = T("weatherMode");
        _windEnabledCheckBox.Content = T("windEnabled");
        _directionLabel.Text = T("windDirection");
        _windSpeedLabel.Text = T("windSpeed");
        _flakeSizeLabel.Text = T("flakeSize");
        _fallSpeedLabel.Text = T("fallSpeed");
        _clearZoneLabel.Text = T("clearZone");
        _clearZoneHint.Text = T("clearZoneHint");
        _cursorInteractionCheckBox.Content = T("cursorInteractionEnabled");
        _cursorInteractionLabel.Text = T("cursorInteractionMode");
        _languageLabel.Text = T("languageLabel");
        _languageHint.Text = T("languageHint");
        _closeButton.Content = T("close");

        _modeBox.ItemsSource = new[] { T("lightSnow"), T("normalSnow"), T("blizzard") };
        _directionBox.ItemsSource = new[] { T("leftToRight"), T("rightToLeft") };
        _cursorModeBox.ItemsSource = new[] { T("cursorScatter"), T("cursorMitten") };
        _languageBox.ItemsSource = new[]
        {
            Localization.LanguageName(AppLanguage.English),
            Localization.LanguageName(AppLanguage.Russian),
            Localization.LanguageName(AppLanguage.Ukrainian)
        };

        _guideText.Text = string.Join("\n\n", new[]
        {
            T("guideIntro"),
            T("guidePause"),
            T("guideClear"),
            T("guideSettings"),
            T("guideSafeZone"),
            T("guideWind"),
            T("guideCursor"),
            T("guideNote")
        });
    }

    private static WpfComboBox CreateComboBox() => new()
    {
        Height = 32,
        MinWidth = 180,
        HorizontalAlignment = WpfHorizontalAlignment.Stretch,
        Margin = new Thickness(0, 2, 0, 8)
    };

    private static Slider CreateSlider(double minimum, double maximum) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        TickFrequency = (maximum - minimum) / 10,
        IsSnapToTickEnabled = false,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static TextBlock CreateLabel() => new()
    {
        FontWeight = FontWeights.Medium,
        Margin = new Thickness(0, 10, 0, 4),
        Foreground = new SolidColorBrush(WpfColor.FromRgb(55, 68, 86))
    };

    private static TextBlock CreateValueText() => new()
    {
        Width = 52,
        TextAlignment = TextAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(WpfColor.FromRgb(65, 78, 96))
    };

    private static void AddSliderRow(WpfPanel parent, Slider slider, TextBlock value, string minimumLabel, string maximumLabel)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 0, 0, 2),
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } }
        };
        Grid.SetColumn(slider, 0);
        Grid.SetColumn(value, 1);
        row.Children.Add(slider);
        row.Children.Add(value);
        parent.Children.Add(row);

        var range = new Grid
        {
            Margin = new Thickness(0, 0, 0, 8),
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
