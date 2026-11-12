namespace Flake.Rendering;

internal enum WeatherMode
{
    LightSnow,
    NormalSnow,
    Blizzard
}

internal enum WindDirection
{
    LeftToRight,
    RightToLeft
}

internal enum CursorInteractionMode
{
    Scatter,
    Mitten
}

internal sealed record WeatherSettings
{
    public WeatherMode Mode { get; init; } = WeatherMode.NormalSnow;
    public WindDirection WindDirection { get; init; } = WindDirection.LeftToRight;
    public Flake.AppLanguage Language { get; init; } = Flake.AppLanguage.English;
    public int ParticleCount { get; init; } = 170;
    public double WindSpeed { get; init; } = 18;
    public double FlakeSize { get; init; } = 1.0;
    public double FallSpeed { get; init; } = 1.0;
    public double ClearZonePercent { get; init; } = 65;
    public bool WindEnabled { get; init; } = true;
    public bool CursorInteractionEnabled { get; init; } = true;
    public CursorInteractionMode CursorMode { get; init; } = CursorInteractionMode.Scatter;

    public static WeatherSettings ForMode(WeatherMode mode) => mode switch
    {
        WeatherMode.LightSnow => new WeatherSettings
        {
            Mode = mode,
            ParticleCount = 90,
            WindSpeed = 8,
            FlakeSize = 0.8,
            FallSpeed = 0.8
        },
        WeatherMode.Blizzard => new WeatherSettings
        {
            Mode = mode,
            ParticleCount = 320,
            WindSpeed = 75,
            FlakeSize = 1.25,
            FallSpeed = 1.45
        },
        _ => new WeatherSettings()
    };

    public WeatherSettings WithModePreservingPreferences(WeatherMode mode)
    {
        var preset = ForMode(mode);
        return preset with
        {
            WindDirection = WindDirection,
            WindEnabled = WindEnabled,
            ClearZonePercent = ClearZonePercent,
            Language = Language,
            CursorInteractionEnabled = CursorInteractionEnabled,
            CursorMode = CursorMode
        };
    }
}
