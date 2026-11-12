using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using WpfPoint = System.Windows.Point;
using WpfColor = System.Windows.Media.Color;

namespace Flake.Rendering;

/// <summary>Draws and animates the desktop snow in a single WPF visual.</summary>
internal sealed class SnowField : FrameworkElement, IDisposable
{
    private readonly List<Snowflake> _particles = [];
    private readonly SnowSurface _snowSurface = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _random = new();
    private TimeSpan _lastFrame;
    private WeatherSettings _settings = new();
    private bool _isPaused;
    private bool _disposed;

    public SnowField()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => ResetParticles();
        SizeChanged += (_, _) => ResetParticles();
        CompositionTarget.Rendering += OnRendering;
    }

    public bool TogglePause()
    {
        _isPaused = !_isPaused;
        _lastFrame = _clock.Elapsed;
        InvalidateVisual();
        return _isPaused;
    }

    public void ApplySettings(WeatherSettings settings)
    {
        var previousParticleCount = _settings.ParticleCount;
        _settings = settings with
        {
            ParticleCount = Math.Clamp(settings.ParticleCount, 20, 600),
            WindSpeed = Math.Clamp(settings.WindSpeed, 0, 120),
            FlakeSize = Math.Clamp(settings.FlakeSize, 0.5, 2.0),
            FallSpeed = Math.Clamp(settings.FallSpeed, 0.5, 2.0)
        };

        if (!IsLoaded)
        {
            return;
        }

        if (_settings.ParticleCount != previousParticleCount)
        {
            while (_particles.Count < _settings.ParticleCount)
            {
                _particles.Add(CreateParticle(randomY: true));
            }

            if (_particles.Count > _settings.ParticleCount)
            {
                _particles.RemoveRange(_settings.ParticleCount, _particles.Count - _settings.ParticleCount);
            }
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        _snowSurface.Draw(drawingContext);

        foreach (var particle in _particles)
        {
            var radius = particle.Radius * _settings.FlakeSize;
            var alpha = (byte)Math.Clamp(particle.Opacity * 255, 35, 245);
            var brush = SnowBrushes.Get(alpha);
            drawingContext.DrawEllipse(
                brush,
                null,
                new WpfPoint(particle.X, particle.Y),
                radius,
                radius);

            if (radius >= 2.2)
            {
                drawingContext.DrawEllipse(
                    SnowBrushes.Highlight,
                    null,
                    new WpfPoint(particle.X - radius * 0.25, particle.Y - radius * 0.25),
                    radius * 0.22,
                    radius * 0.22);
            }
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_disposed || _isPaused || _particles.Count == 0)
        {
            return;
        }

        var now = _clock.Elapsed;
        var delta = Math.Clamp((now - _lastFrame).TotalSeconds, 0, 0.05);
        _lastFrame = now;
        if (delta <= 0)
        {
            return;
        }

        var elapsed = now.TotalSeconds;
        var width = Math.Max(ActualWidth, 1);
        var height = Math.Max(ActualHeight, 1);
        var windSign = _settings.WindDirection == WindDirection.LeftToRight ? 1 : -1;
        var windVelocity = _settings.WindSpeed * windSign;

        foreach (var particle in _particles)
        {
            var radius = particle.Radius * _settings.FlakeSize;
            var nextY = particle.Y + particle.FallSpeed * _settings.FallSpeed * delta;
            var nextX = particle.X + (Math.Sin(elapsed * 0.8 + particle.Phase) * particle.Drift + windVelocity) * delta;

            if (nextX < -radius)
            {
                nextX = width + radius;
            }
            else if (nextX > width + radius)
            {
                nextX = -radius;
            }

            particle.X = nextX;
            var surfaceHeight = _snowSurface.GetHeightAt(Math.Clamp(nextX, 0, width));
            var surfaceY = height - surfaceHeight;

            if (nextY + radius >= surfaceY)
            {
                _snowSurface.Accumulate(Math.Clamp(nextX, 0, width), radius);
                particle.Y = -radius - _random.NextDouble() * height * 0.15;
                particle.X = _random.NextDouble() * width;
                continue;
            }

            particle.Y = nextY;
        }

        InvalidateVisual();
    }

    private void ResetParticles()
    {
        if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        _snowSurface.Resize(ActualWidth, ActualHeight);
        _particles.Clear();
        for (var i = 0; i < _settings.ParticleCount; i++)
        {
            _particles.Add(CreateParticle(randomY: true));
        }

        _lastFrame = _clock.Elapsed;
        InvalidateVisual();
    }

    public void ClearAccumulatedSnow()
    {
        _snowSurface.Clear();
        InvalidateVisual();
    }

    private Snowflake CreateParticle(bool randomY)
    {
        return new Snowflake
        {
            X = _random.NextDouble() * Math.Max(ActualWidth, 1),
            Y = randomY ? _random.NextDouble() * Math.Max(ActualHeight, 1) : -5,
            Radius = 0.7 + _random.NextDouble() * 2.1,
            FallSpeed = 28 + _random.NextDouble() * 72,
            Drift = 5 + _random.NextDouble() * 18,
            Phase = _random.NextDouble() * Math.PI * 2,
            Opacity = 0.38 + _random.NextDouble() * 0.55
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CompositionTarget.Rendering -= OnRendering;
    }

    private static class SnowBrushes
    {
        private static readonly Dictionary<byte, SolidColorBrush> Cache = [];

        public static SolidColorBrush Highlight { get; } = Create(255, 255, 255, 210);

        public static SolidColorBrush Get(byte alpha)
        {
            if (Cache.TryGetValue(alpha, out var brush))
            {
                return brush;
            }

            brush = Create(255, 255, 255, alpha);
            Cache[alpha] = brush;
            return brush;
        }

        private static SolidColorBrush Create(byte red, byte green, byte blue, byte alpha)
        {
            var brush = new SolidColorBrush(WpfColor.FromArgb(alpha, red, green, blue));
            brush.Freeze();
            return brush;
        }
    }
}
