using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Flake.Interop;
using WpfPoint = System.Windows.Point;
using WpfColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;

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
    private WpfPoint _cursorPosition;
    private WpfPoint _previousCursorPosition;
    private bool _hasCursorPosition;
    private bool _hasPreviousCursorPosition;
    private double _cursorMovementDistance;

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
            FallSpeed = Math.Clamp(settings.FallSpeed, 0.5, 2.0),
            ClearZonePercent = Math.Clamp(settings.ClearZonePercent, 50, 90)
        };
        _snowSurface.SetClearZonePercent(_settings.ClearZonePercent);
        if (!_settings.CursorInteractionEnabled)
        {
            _hasCursorPosition = false;
            _hasPreviousCursorPosition = false;
        }

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

        if (_settings.CursorInteractionEnabled &&
            _settings.CursorMode == CursorInteractionMode.Mitten &&
            _hasCursorPosition)
        {
            DrawMitten(drawingContext, _cursorPosition);
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_disposed || !IsLoaded)
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

        var clearAnimationChanged = _snowSurface.UpdateClearAnimation(delta);
        var cursorChanged = UpdateCursorState();
        var width = Math.Max(ActualWidth, 1);
        var height = Math.Max(ActualHeight, 1);

        var snowSwept = false;
        if (_settings.CursorInteractionEnabled &&
            _settings.CursorMode == CursorInteractionMode.Mitten &&
            _hasCursorPosition &&
            _hasPreviousCursorPosition &&
            _cursorMovementDistance > 1.5)
        {
            snowSwept = SweepSnowAlongCursorPath(width, height, delta);
        }

        if (!_isPaused && _particles.Count > 0)
        {
            var elapsed = now.TotalSeconds;
            var windSign = _settings.WindDirection == WindDirection.LeftToRight ? 1 : -1;

            foreach (var particle in _particles)
            {
                var radius = particle.Radius * _settings.FlakeSize;
                var gust = 0.65 + 0.35 * Math.Sin(elapsed * 0.42 + particle.Phase * 0.2);
                var windVelocity = _settings.WindEnabled ? _settings.WindSpeed * windSign * gust : 0;
                var naturalDrift = Math.Sin(elapsed * 0.8 + particle.Phase) * particle.Drift;

                ApplyCursorForce(particle, delta);
                var nextY = particle.Y + (particle.FallSpeed * _settings.FallSpeed + particle.VelocityY) * delta;
                var nextX = particle.X + (naturalDrift + windVelocity + particle.VelocityX) * delta;
                var damping = Math.Exp(-3.8 * delta);
                particle.VelocityX *= damping;
                particle.VelocityY *= damping;

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
                    particle.VelocityX = 0;
                    particle.VelocityY = 0;
                    continue;
                }

                particle.Y = nextY;
            }
        }

        if (clearAnimationChanged || !_isPaused || cursorChanged || snowSwept)
        {
            InvalidateVisual();
        }
    }

    private bool UpdateCursorState()
    {
        if (!_settings.CursorInteractionEnabled || !CursorInput.TryGetPosition(out var screenPosition))
        {
            var hadCursor = _hasCursorPosition;
            _hasCursorPosition = false;
            _hasPreviousCursorPosition = false;
            _cursorMovementDistance = 0;
            return hadCursor;
        }

        var localPosition = PointFromScreen(screenPosition);
        var wasVisible = _hasCursorPosition;
        var dx = localPosition.X - _cursorPosition.X;
        var dy = localPosition.Y - _cursorPosition.Y;
        _cursorMovementDistance = wasVisible ? Math.Sqrt(dx * dx + dy * dy) : 0;
        var moved = !wasVisible || _cursorMovementDistance > 0.5;

        _previousCursorPosition = _cursorPosition;
        _hasPreviousCursorPosition = wasVisible;
        _cursorPosition = localPosition;
        _hasCursorPosition = true;
        return moved;
    }

    private void ApplyCursorForce(Snowflake particle, double delta)
    {
        if (!_settings.CursorInteractionEnabled || !_hasCursorPosition)
        {
            return;
        }

        var mittenMode = _settings.CursorMode == CursorInteractionMode.Mitten;
        var influenceRadius = mittenMode ? 112.0 : 88.0;
        var dx = particle.X - _cursorPosition.X;
        var dy = particle.Y - _cursorPosition.Y;
        var distanceSquared = dx * dx + dy * dy;

        if (distanceSquared >= influenceRadius * influenceRadius || distanceSquared < 0.01)
        {
            return;
        }

        var distance = Math.Sqrt(distanceSquared);
        var falloff = 1 - distance / influenceRadius;
        var force = falloff * (mittenMode ? 980.0 : 720.0) * delta;
        particle.VelocityX += dx / distance * force;
        particle.VelocityY += dy / distance * force;
    }

    private bool SweepSnowAlongCursorPath(double width, double height, double delta)
    {
        var changed = false;
        var start = _hasPreviousCursorPosition ? _previousCursorPosition : _cursorPosition;
        var dx = _cursorPosition.X - start.X;
        var dy = _cursorPosition.Y - start.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var steps = Math.Clamp((int)Math.Ceiling(distance / 12.0), 1, 24);
        var removalStrength = 1.35 * Math.Clamp(delta * 60, 0.25, 3.0);

        for (var step = 0; step <= steps; step++)
        {
            var progress = (double)step / steps;
            var x = Math.Clamp(start.X + dx * progress, 0, width);
            var y = start.Y + dy * progress;
            var surfaceHeight = _snowSurface.GetHeightAt(x);
            if (surfaceHeight <= 0.5)
            {
                continue;
            }

            var surfaceY = height - surfaceHeight;
            if (y >= surfaceY - 30 && y <= height + 36)
            {
                changed |= _snowSurface.Sweep(x, radius: 25, strength: removalStrength);
            }
        }

        return changed;
    }

    private static void DrawMitten(DrawingContext drawingContext, WpfPoint cursor)
    {
        const double offsetX = 12;
        const double offsetY = 12;
        var center = new WpfPoint(cursor.X + offsetX, cursor.Y + offsetY);
        drawingContext.PushTransform(new RotateTransform(-28, center.X, center.Y));

        var palm = new Rect(center.X - 7, center.Y - 13, 15, 22);
        drawingContext.DrawRoundedRectangle(SnowBrushes.MittenFill, SnowBrushes.MittenOutline, palm, 7, 7);
        drawingContext.DrawEllipse(SnowBrushes.MittenFill, SnowBrushes.MittenOutline,
            new WpfPoint(center.X - 10, center.Y - 3), 5, 7);
        drawingContext.DrawRoundedRectangle(SnowBrushes.MittenCuff, SnowBrushes.MittenOutline,
            new Rect(center.X - 7, center.Y + 6, 15, 6), 2, 2);
        drawingContext.DrawLine(SnowBrushes.MittenHighlight,
            new WpfPoint(center.X - 3, center.Y - 8), new WpfPoint(center.X - 3, center.Y + 2));
        drawingContext.Pop();
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
        _snowSurface.BeginAnimatedClear();
        _lastFrame = _clock.Elapsed;
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
        public static readonly SolidColorBrush MittenFill = Create(220, 238, 250, 235);
        public static readonly SolidColorBrush MittenCuff = Create(139, 181, 216, 245);
        public static readonly WpfPen MittenOutline = CreatePen(73, 112, 145, 235, 1.3);
        public static readonly WpfPen MittenHighlight = CreatePen(255, 255, 255, 190, 1);

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

        public static WpfPen CreatePen(byte red, byte green, byte blue, byte alpha, double thickness)
        {
            var pen = new WpfPen(Create(red, green, blue, alpha), thickness);
            pen.Freeze();
            return pen;
        }

        private static SolidColorBrush Create(byte red, byte green, byte blue, byte alpha)
        {
            var brush = new SolidColorBrush(WpfColor.FromArgb(alpha, red, green, blue));
            brush.Freeze();
            return brush;
        }
    }
}
