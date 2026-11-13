using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Flake.Interop;
using WpfPoint = System.Windows.Point;
using WpfColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;

namespace Flake.Rendering;

/// <summary>Draws and animates the desktop snow and its interactive objects in one WPF visual.</summary>
internal sealed class SnowField : FrameworkElement, IDisposable
{
    private const double SnowballGravity = 1250.0;
    private const double MaximumSnowballRadius = 56.0;
    private const double MinimumFormedSnowballRadius = 4.0;
    private const int MaximumSnowballCount = 24;
    private const double SnowballHitTestPadding = 5.0;

    private readonly List<Snowflake> _particles = [];
    private readonly List<Snowball> _snowballs = [];
    private readonly List<Snowman> _snowmen = [];
    private readonly SnowSurface _snowSurface = new();
    private readonly WindowSnowSurface _windowSnowSurface = new();
    private readonly SnowfallState _snowfallState = new();
    private TimeSpan _lastWindowRefresh;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _random = new();
    private TimeSpan _lastFrame;
    private WeatherSettings _settings = new();
    private bool _disposed;
    private WpfPoint _cursorPosition;
    private WpfPoint _pointerPosition;
    private bool _hasCursorPosition;
    private bool _hasPointerPosition;
    private bool _isClickThrough = true;
    private bool _isGatheringSnow;
    private double _gatheredSnowArea;
    private WpfPoint _lastGatherPosition;
    private TimeSpan _lastGatherTime;
    private Snowball? _formingSnowball;
    private Snowball? _draggedSnowball;
    private Snowman? _draggedSnowman;
    private WpfPoint _dragOffset;
    private TimeSpan _lastDragTime;

    /// <summary>Raised when the native overlay must start or stop passing mouse input through.</summary>
    public event Action<bool>? ClickThroughChanged;

    public SnowField()
    {
        IsHitTestVisible = true;
        _windowSnowSurface.SnowDropped += SpawnFallingSnow;
        Loaded += (_, _) => ResetParticles();
        SizeChanged += (_, _) => HandleViewportSizeChanged();
        MouseLeftButtonDown += HandleMouseLeftButtonDown;
        MouseMove += HandleMouseMove;
        MouseLeave += HandleMouseLeave;
        MouseLeftButtonUp += HandleMouseLeftButtonUp;
        LostMouseCapture += HandleLostMouseCapture;
        CompositionTarget.Rendering += OnRendering;
    }

    public bool TogglePause()
    {
        var isPaused = _snowfallState.TogglePause();
        _lastFrame = _clock.Elapsed;
        InvalidateVisual();
        return isPaused;
    }

    public bool ToggleSnowfallGeneration()
    {
        var isGenerating = _snowfallState.ToggleGeneration();
        if (isGenerating && IsLoaded)
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
        return isGenerating;
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
        }

        if (!IsLoaded)
        {
            return;
        }

        if (_settings.ParticleCount != previousParticleCount && _snowfallState.IsGenerating)
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

        UpdateClickThroughState();
        InvalidateVisual();
    }

    /// <summary>Returns true when the pointer is inside the snow-gathering hit-test band.</summary>
    private bool IsPointerOverGatherableSnow(WpfPoint position)
    {
        if (!_settings.CursorInteractionEnabled ||
            _settings.CursorMode != CursorInteractionMode.Mitten ||
            _snowSurface.IsClearing ||
            _snowballs.Count + _snowmen.Count * 3 >= MaximumSnowballCount ||
            TryFindSnowballAt(position, out _))
        {
            return false;
        }

        var x = Math.Clamp(position.X, 0, Math.Max(ActualWidth, 1));
        var surfaceHeight = _snowSurface.GetHeightAt(x);
        if (surfaceHeight < 1.0)
        {
            return false;
        }

        var surfaceY = ActualHeight - surfaceHeight;
        return position.Y >= surfaceY - 38 && position.Y <= surfaceY + 24;
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var point = hitTestParameters.HitPoint;
        return TryFindSnowballAt(point, out _) || IsPointerOverGatherableSnow(point)
            ? new PointHitTestResult(this, point)
            : null!;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        _snowSurface.Draw(drawingContext);
        _windowSnowSurface.Draw(drawingContext);

        foreach (var snowball in _snowballs)
        {
            DrawSnowball(drawingContext, snowball);
        }

        foreach (var snowman in _snowmen)
        {
            DrawSnowball(drawingContext, snowman.Bottom);
            DrawSnowball(drawingContext, snowman.Middle);
            DrawSnowball(drawingContext, snowman.Top);
            snowman.DrawDetails(drawingContext);
        }

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

        if (_formingSnowball is not null)
        {
            DrawSnowball(drawingContext, _formingSnowball);
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
        if (now - _lastWindowRefresh >= TimeSpan.FromMilliseconds(350))
        {
            _windowSnowSurface.Refresh(PointFromScreen, Environment.ProcessId, width, height);
            _lastWindowRefresh = now;
        }

        if (_snowfallState.ShouldAdvanceExistingParticles && _particles.Count > 0)
        {
            var elapsed = now.TotalSeconds;
            var windSign = _settings.WindDirection == WindDirection.LeftToRight ? 1 : -1;

            for (var particleIndex = _particles.Count - 1; particleIndex >= 0; particleIndex--)
            {
                var particle = _particles[particleIndex];
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

                if (_windowSnowSurface.TryLandFlake(particle.X, particle.Y + radius, nextX, nextY + radius, radius, width))
                {
                    if (_snowfallState.ShouldRespawnLandedParticles)
                    {
                        RespawnParticle(particle, radius, width, height);
                    }
                    else
                    {
                        _particles.RemoveAt(particleIndex);
                    }
                    continue;
                }

                var surfaceHeight = _snowSurface.GetHeightAt(Math.Clamp(nextX, 0, width));
                var surfaceY = height - surfaceHeight;

                if (nextY + radius >= surfaceY)
                {
                    _snowSurface.Accumulate(Math.Clamp(nextX, 0, width), radius);
                    if (_snowfallState.ShouldRespawnLandedParticles)
                    {
                        RespawnParticle(particle, radius, width, height);
                    }
                    else
                    {
                        _particles.RemoveAt(particleIndex);
                    }
                    continue;
                }

                if (nextY - radius > height)
                {
                    _particles.RemoveAt(particleIndex);
                    continue;
                }

                particle.X = nextX;
                particle.Y = nextY;
            }
        }

        var objectsChanged = false;
        if (_snowfallState.ShouldAdvanceExistingParticles && (_snowballs.Count > 0 || _snowmen.Count > 0))
        {
            UpdateSnowballWorld(delta, width, height);
            objectsChanged = true;
        }

        UpdateClickThroughState();

        if (clearAnimationChanged || _snowfallState.ShouldAdvanceExistingParticles || cursorChanged || objectsChanged)
        {
            InvalidateVisual();
        }
    }

    private void RespawnParticle(Snowflake particle, double radius, double width, double height)
    {
        particle.Y = -radius - _random.NextDouble() * height * 0.15;
        particle.X = _random.NextDouble() * width;
        particle.VelocityX = 0;
        particle.VelocityY = 0;
    }

    private void StartSnowGathering(WpfPoint position)
    {
        _isGatheringSnow = true;
        _gatheredSnowArea = 0;
        _lastGatherPosition = position;
        _lastGatherTime = _clock.Elapsed;
        _formingSnowball = new Snowball
        {
            X = position.X + 28,
            Y = position.Y + 8,
            Radius = 0
        };
        SetClickThrough(false);
    }

    private bool GatherSnowAlongCursorPath(WpfPoint position, double width, double height, double delta)
    {
        if (_formingSnowball is null)
        {
            return false;
        }

        var dx = position.X - _lastGatherPosition.X;
        var dy = position.Y - _lastGatherPosition.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var steps = Math.Clamp((int)Math.Ceiling(distance / 9.0), 1, 24);
        var brushRadius = 24 + Math.Min(_formingSnowball.Radius, 40) * 0.30;
        var depth = 0.85 * Math.Clamp(delta * 60, 0.35, 2.0);
        var collectedThisFrame = 0.0;

        if (_formingSnowball.Radius < MaximumSnowballRadius - 0.01)
        {
            for (var step = 1; step <= steps; step++)
            {
                var progress = (double)step / steps;
                var x = Math.Clamp(_lastGatherPosition.X + dx * progress, 0, width);
                var y = _lastGatherPosition.Y + dy * progress;
                var surfaceHeight = _snowSurface.GetHeightAt(x);
                var surfaceY = height - surfaceHeight;

                if (surfaceHeight < 1.0 || y < surfaceY - 38 || y > surfaceY + 24)
                {
                    continue;
                }

                collectedThisFrame += _snowSurface.CollectSnow(x, brushRadius, depth);
            }
        }

        _gatheredSnowArea += collectedThisFrame;
        _formingSnowball.Radius = Math.Clamp(
            Math.Sqrt(_gatheredSnowArea / Math.PI) * 0.72,
            0,
            MaximumSnowballRadius);
        _formingSnowball.X = Math.Clamp(
            position.X + 28,
            _formingSnowball.Radius + 2,
            Math.Max(_formingSnowball.Radius + 2, width - _formingSnowball.Radius - 2));
        _formingSnowball.Y = Math.Clamp(
            position.Y + 8,
            _formingSnowball.Radius + 2,
            Math.Max(_formingSnowball.Radius + 2, height - _formingSnowball.Radius - 2));
        _lastGatherPosition = position;
        return collectedThisFrame > 0.001 || distance > 0.5;
    }

    private void FinishSnowGathering(double width, double height, bool releaseCapture = true)
    {
        if (!_isGatheringSnow)
        {
            return;
        }

        _isGatheringSnow = false;
        var ball = _formingSnowball;
        _formingSnowball = null;

        if (ball is not null && ball.Radius >= MinimumFormedSnowballRadius &&
            _snowballs.Count + _snowmen.Count * 3 < MaximumSnowballCount)
        {
            ball.X = Math.Clamp(ball.X, ball.Radius + 2, Math.Max(ball.Radius + 2, width - ball.Radius - 2));
            var surfaceY = height - _snowSurface.GetHeightAt(ball.X);
            var restingCenterY = surfaceY - ball.Radius;
            ball.Y = Math.Clamp(Math.Min(ball.Y, restingCenterY), ball.Radius + 2, height - ball.Radius);
            ball.VelocityX = 0;
            ball.VelocityY = 0;
            _snowballs.Add(ball);
        }

        _gatheredSnowArea = 0;
        if (releaseCapture && IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        UpdateClickThroughState();
        InvalidateVisual();
    }

    private void UpdateSnowballWorld(double delta, double width, double height)
    {
        // Substep fast motion so small snowballs cannot tunnel through one another between frames.
        var stepCount = Math.Clamp((int)Math.Ceiling(delta / (1.0 / 240.0)), 1, 16);
        var stepDelta = delta / stepCount;

        for (var step = 0; step < stepCount; step++)
        {
            foreach (var snowball in _snowballs)
            {
                if (!ReferenceEquals(snowball, _draggedSnowball))
                {
                    UpdateSnowballMotion(snowball, stepDelta, width, height);
                }
            }

            foreach (var snowman in _snowmen)
            {
                if (!ReferenceEquals(snowman, _draggedSnowman))
                {
                    UpdateSnowballMotion(snowman.Bottom, stepDelta, width, height);
                    snowman.AlignParts();
                }
            }

            ResolveSnowballCollisions();
            _snowballs.RemoveAll(ball => ball.IsBroken);
            _snowmen.RemoveAll(man => man.Bottom.IsBroken || man.Middle.IsBroken || man.Top.IsBroken);
        }

        TryAssembleSnowman(height);
    }

    private void UpdateSnowballMotion(Snowball snowball, double delta, double width, double height)
    {
        var radius = snowball.Radius;
        var previousX = snowball.X;
        var previousY = snowball.Y;
        var slopeProbe = Math.Max(4, radius * 0.45);
        var leftX = Math.Max(0, snowball.X - slopeProbe);
        var rightX = Math.Min(width, snowball.X + slopeProbe);
        var slope = rightX - leftX < 0.1
            ? 0
            : (_snowSurface.GetHeightAt(rightX) - _snowSurface.GetHeightAt(leftX)) / (rightX - leftX);

        var surfaceHeight = _snowSurface.GetHeightAt(snowball.X);
        var groundCenterY = height - surfaceHeight - radius;
        var isWindowGrounded = _windowSnowSurface.TryGetTopSurfaceAt(snowball.X, snowball.Y, radius, out var windowSurfaceY) &&
            snowball.VelocityY >= -35;
        var isGrounded = isWindowGrounded || (snowball.Y >= groundCenterY - 1.0 && snowball.VelocityY >= -35);

        if (isGrounded)
        {
            snowball.Y = isWindowGrounded ? windowSurfaceY - radius : groundCenterY;
            snowball.VelocityY = 0;
            snowball.VelocityX += -SnowballGravity * slope / (1 + slope * slope) * delta;
            snowball.VelocityX *= Math.Exp(-1.65 * delta);

            if (Math.Abs(snowball.VelocityX) < 4 && Math.Abs(slope) < 0.012)
            {
                snowball.VelocityX = 0;
            }

            if (Math.Abs(snowball.VelocityX) > 16 && surfaceHeight > 0.5)
            {
                var speedFactor = Math.Clamp(Math.Abs(snowball.VelocityX) / 80, 0.15, 1.0);
                var collectedArea = _snowSurface.CollectSnow(
                    snowball.X,
                    radius,
                    depth: 14 * delta * speedFactor);
                snowball.Radius = Math.Min(MaximumSnowballRadius, radius + collectedArea * 0.0012);
            }

            snowball.Rotation += snowball.VelocityX / Math.Max(snowball.Radius, 1) * delta;
        }
        else
        {
            snowball.VelocityY = Math.Min(snowball.VelocityY + SnowballGravity * delta, 950);
        }

        snowball.X += snowball.VelocityX * delta;
        snowball.Y += snowball.VelocityY * delta;

        if (snowball.X < radius)
        {
            snowball.X = radius;
            snowball.VelocityX = Math.Abs(snowball.VelocityX) * 0.38;
        }
        else if (snowball.X > width - radius)
        {
            snowball.X = Math.Max(radius, width - radius);
            snowball.VelocityX = -Math.Abs(snowball.VelocityX) * 0.38;
        }

        if (_windowSnowSurface.TryHitBallEdge(previousX, previousY, snowball.X, snowball.Y, radius, out var windowHit))
        {
            var impactSpeed = windowHit.Edge == WindowSnowEdge.Top
                ? Math.Abs(snowball.VelocityY)
                : Math.Abs(snowball.VelocityX);
            if (impactSpeed >= 520)
            {
                _windowSnowSurface.DepositImpact(windowHit, Math.Max(2.5, radius * 0.42));
                SpawnImpactFragments(snowball.X, snowball.Y, radius);
                snowball.IsBroken = true;
                return;
            }

            if (windowHit.Edge == WindowSnowEdge.Top)
            {
                snowball.Y = windowHit.Surface - radius;
                snowball.VelocityY = 0;
                snowball.VelocityX *= 0.82;
            }
            else if (windowHit.Edge == WindowSnowEdge.Left)
            {
                snowball.X = windowHit.Surface - radius;
                snowball.VelocityX = 0;
            }
            else
            {
                snowball.X = windowHit.Surface + radius;
                snowball.VelocityX = 0;
            }
        }

        surfaceHeight = _snowSurface.GetHeightAt(snowball.X);
        groundCenterY = height - surfaceHeight - snowball.Radius;
        if (snowball.Y >= groundCenterY && snowball.VelocityY >= 0)
        {
            var impactSpeed = snowball.VelocityY;
            snowball.Y = groundCenterY;
            if (impactSpeed >= 520)
            {
                _snowSurface.Accumulate(snowball.X - radius * 0.45, radius * 0.35);
                _snowSurface.Accumulate(snowball.X, radius * 0.55);
                _snowSurface.Accumulate(snowball.X + radius * 0.45, radius * 0.35);
                SpawnImpactFragments(snowball.X, snowball.Y, radius);
                snowball.IsBroken = true;
                return;
            }

            snowball.VelocityY = impactSpeed > 95 ? -impactSpeed * 0.12 : 0;
        }
    }

    private void ResolveSnowballCollisions()
    {
        for (var firstIndex = 0; firstIndex < _snowballs.Count; firstIndex++)
        {
            var first = _snowballs[firstIndex];
            if (ReferenceEquals(first, _draggedSnowball))
            {
                continue;
            }

            for (var secondIndex = firstIndex + 1; secondIndex < _snowballs.Count; secondIndex++)
            {
                var second = _snowballs[secondIndex];
                if (ReferenceEquals(second, _draggedSnowball))
                {
                    continue;
                }

                var dx = second.X - first.X;
                var dy = second.Y - first.Y;
                var minimumDistance = first.Radius + second.Radius;
                var distanceSquared = dx * dx + dy * dy;

                if (distanceSquared >= minimumDistance * minimumDistance)
                {
                    continue;
                }

                var distance = Math.Sqrt(Math.Max(distanceSquared, 0.0001));
                var normalX = distanceSquared < 0.0001 ? 1 : dx / distance;
                var normalY = distanceSquared < 0.0001 ? 0 : dy / distance;
                var penetration = minimumDistance - distance;
                var inverseMassFirst = 1 / first.Mass;
                var inverseMassSecond = 1 / second.Mass;
                var inverseMassTotal = inverseMassFirst + inverseMassSecond;

                first.X -= normalX * penetration * inverseMassFirst / inverseMassTotal;
                first.Y -= normalY * penetration * inverseMassFirst / inverseMassTotal;
                second.X += normalX * penetration * inverseMassSecond / inverseMassTotal;
                second.Y += normalY * penetration * inverseMassSecond / inverseMassTotal;

                var relativeVelocityX = second.VelocityX - first.VelocityX;
                var relativeVelocityY = second.VelocityY - first.VelocityY;
                var normalVelocity = relativeVelocityX * normalX + relativeVelocityY * normalY;

                if (normalVelocity < -650)
                {
                    first.IsBroken = true;
                    second.IsBroken = true;
                    SpawnImpactFragments((first.X + second.X) * 0.5, (first.Y + second.Y) * 0.5,
                        Math.Min(first.Radius, second.Radius));
                    continue;
                }

                if (normalVelocity < 0)
                {
                    const double restitution = 0.30;
                    var impulse = -(1 + restitution) * normalVelocity / inverseMassTotal;
                    first.VelocityX -= impulse * normalX * inverseMassFirst;
                    first.VelocityY -= impulse * normalY * inverseMassFirst;
                    second.VelocityX += impulse * normalX * inverseMassSecond;
                    second.VelocityY += impulse * normalY * inverseMassSecond;
                }
            }
        }

        // Snowmen are composite bodies; each visible ball is part of the same movable mass.
        foreach (var ball in _snowballs)
        {
            if (ball.IsBroken || ReferenceEquals(ball, _draggedSnowball))
            {
                continue;
            }

            foreach (var snowman in _snowmen)
            {
                if (ball.IsBroken)
                {
                    break;
                }

                if (ReferenceEquals(snowman, _draggedSnowman) ||
                    snowman.Bottom.IsBroken || snowman.Middle.IsBroken || snowman.Top.IsBroken)
                {
                    continue;
                }

                ResolveSnowballSnowmanCollision(ball, snowman, snowman.Top);
                if (!ball.IsBroken) ResolveSnowballSnowmanCollision(ball, snowman, snowman.Middle);
                if (!ball.IsBroken) ResolveSnowballSnowmanCollision(ball, snowman, snowman.Bottom);
            }
        }
    }

    private void ResolveSnowballSnowmanCollision(Snowball ball, Snowman snowman, Snowball part)
    {
        var dx = ball.X - part.X;
        var dy = ball.Y - part.Y;
        var minimumDistance = ball.Radius + part.Radius;
        var distanceSquared = dx * dx + dy * dy;
        if (distanceSquared >= minimumDistance * minimumDistance)
        {
            return;
        }

        var distance = Math.Sqrt(Math.Max(distanceSquared, 0.0001));
        var normalX = distanceSquared < 0.0001 ? 1 : dx / distance;
        var normalY = distanceSquared < 0.0001 ? 0 : dy / distance;
        var penetration = minimumDistance - distance;
        var inverseMassBall = 1 / ball.Mass;
        var inverseMassSnowman = 1 / snowman.Mass;
        var inverseMassTotal = inverseMassBall + inverseMassSnowman;

        ball.X += normalX * penetration * inverseMassBall / inverseMassTotal;
        ball.Y += normalY * penetration * inverseMassBall / inverseMassTotal;
        snowman.Translate(
            -normalX * penetration * inverseMassSnowman / inverseMassTotal,
            -normalY * penetration * inverseMassSnowman / inverseMassTotal);

        var relativeVelocityX = ball.VelocityX - part.VelocityX;
        var relativeVelocityY = ball.VelocityY - part.VelocityY;
        var normalVelocity = relativeVelocityX * normalX + relativeVelocityY * normalY;

        if (normalVelocity < -650)
        {
            ball.IsBroken = true;
            snowman.Bottom.IsBroken = true;
            snowman.Middle.IsBroken = true;
            snowman.Top.IsBroken = true;
            SpawnImpactFragments((ball.X + part.X) * 0.5, (ball.Y + part.Y) * 0.5,
                Math.Min(ball.Radius, part.Radius));
            return;
        }

        if (normalVelocity < 0)
        {
            const double restitution = 0.24;
            var impulse = -(1 + restitution) * normalVelocity / inverseMassTotal;
            ball.VelocityX += impulse * normalX * inverseMassBall;
            ball.VelocityY += impulse * normalY * inverseMassBall;
            snowman.Bottom.VelocityX -= impulse * normalX * inverseMassSnowman;
            snowman.Bottom.VelocityY -= impulse * normalY * inverseMassSnowman;
            snowman.AlignParts();
        }
    }

    private void TryAssembleSnowman(double height)
    {
        for (var bottomIndex = 0; bottomIndex < _snowballs.Count; bottomIndex++)
        {
            var bottom = _snowballs[bottomIndex];
            if (bottom.Radius < 28 || !IsRestingOnSurface(bottom, height))
            {
                continue;
            }

            for (var middleIndex = 0; middleIndex < _snowballs.Count; middleIndex++)
            {
                if (middleIndex == bottomIndex)
                {
                    continue;
                }

                var middle = _snowballs[middleIndex];
                if (middle.Radius < bottom.Radius * 0.58 ||
                    middle.Radius > bottom.Radius * 0.86 ||
                    !AreStacked(bottom, middle))
                {
                    continue;
                }

                for (var topIndex = 0; topIndex < _snowballs.Count; topIndex++)
                {
                    if (topIndex == bottomIndex || topIndex == middleIndex)
                    {
                        continue;
                    }

                    var top = _snowballs[topIndex];
                    if (top.Radius < 14 ||
                        top.Radius < middle.Radius * 0.58 ||
                        top.Radius > middle.Radius * 0.86 ||
                        !AreStacked(middle, top))
                    {
                        continue;
                    }

                    _snowmen.Add(new Snowman(bottom, middle, top));
                    _snowballs.Remove(bottom);
                    _snowballs.Remove(middle);
                    _snowballs.Remove(top);
                    return;
                }
            }
        }
    }

    private bool IsRestingOnSurface(Snowball snowball, double height)
    {
        var expectedY = height - _snowSurface.GetHeightAt(snowball.X) - snowball.Radius;
        var restsOnDesktop = Math.Abs(snowball.Y - expectedY) <= 5;
        var restsOnWindow = _windowSnowSurface.TryGetTopSurfaceAt(snowball.X, snowball.Y, snowball.Radius, out _);
        return (restsOnDesktop || restsOnWindow) &&
               Math.Abs(snowball.VelocityY) < 60 &&
               Math.Abs(snowball.VelocityX) < 75;
    }

    private static bool AreStacked(Snowball lower, Snowball upper)
    {
        var horizontalTolerance = Math.Max(7, Math.Min(lower.Radius, upper.Radius) * 0.25);
        var expectedVerticalDistance = lower.Radius + upper.Radius;
        var actualVerticalDistance = lower.Y - upper.Y;
        var verticalTolerance = Math.Max(9, upper.Radius * 0.4);

        return upper.Y < lower.Y &&
               Math.Abs(upper.X - lower.X) <= horizontalTolerance &&
               Math.Abs(actualVerticalDistance - expectedVerticalDistance) <= verticalTolerance &&
               Math.Abs(lower.VelocityX) < 75 &&
               Math.Abs(upper.VelocityX) < 75 &&
               Math.Abs(upper.VelocityY) < 75;
    }

    private bool UpdateCursorState()
    {
        var hadCursor = _hasCursorPosition;
        if (!CursorInput.TryGetPosition(out var screenPosition))
        {
            _hasPointerPosition = false;
            _hasCursorPosition = false;
            UpdateClickThroughState();
            return hadCursor;
        }

        var localPosition = PointFromScreen(screenPosition);
        _pointerPosition = localPosition;
        _hasPointerPosition = true;

        if (!_settings.CursorInteractionEnabled)
        {
            _hasCursorPosition = false;
            UpdateClickThroughState();
            return hadCursor;
        }

        var dx = localPosition.X - _cursorPosition.X;
        var dy = localPosition.Y - _cursorPosition.Y;
        var moved = !hadCursor || dx * dx + dy * dy > 0.25;
        _cursorPosition = localPosition;
        _hasCursorPosition = true;
        UpdateClickThroughState();
        return moved;
    }

    private void UpdateClickThroughState()
    {
        var pointerOverInteractiveSnow = _hasPointerPosition &&
            (IsPointerOverSnowball(_pointerPosition) || IsPointerOverGatherableSnow(_pointerPosition));
        SetClickThrough(_draggedSnowball is null && !_isGatheringSnow && !pointerOverInteractiveSnow);
    }

    private void SetClickThrough(bool clickThrough)
    {
        if (_isClickThrough == clickThrough)
        {
            return;
        }

        _isClickThrough = clickThrough;
        ClickThroughChanged?.Invoke(clickThrough);
    }

    private bool IsPointerOverSnowball(WpfPoint point) =>
        TryFindSnowballAt(point, out _);

    private bool TryFindSnowballAt(WpfPoint point, out Snowball snowball)
    {
        for (var index = _snowmen.Count - 1; index >= 0; index--)
        {
            if (_snowmen[index].TryHitTest(point, SnowballHitTestPadding, out snowball))
            {
                return true;
            }
        }

        for (var index = _snowballs.Count - 1; index >= 0; index--)
        {
            if (IsInsideSnowball(point, _snowballs[index]))
            {
                snowball = _snowballs[index];
                return true;
            }
        }

        snowball = null!;
        return false;
    }

    private static bool IsInsideSnowball(WpfPoint point, Snowball snowball)
    {
        var dx = point.X - snowball.X;
        var dy = point.Y - snowball.Y;
        var hitRadius = snowball.Radius + SnowballHitTestPadding;
        return dx * dx + dy * dy <= hitRadius * hitRadius;
    }

    private Snowman? FindSnowman(Snowball snowball) =>
        _snowmen.FirstOrDefault(candidate => candidate.Contains(snowball));

    private static double ClampGroupOffset(double offset, double minimum, double maximum, double viewportSize)
    {
        var lowerBound = -minimum;
        var upperBound = viewportSize - maximum;
        if (lowerBound > upperBound)
        {
            return (lowerBound + upperBound) * 0.5;
        }

        return Math.Clamp(offset, lowerBound, upperBound);
    }

    private void HandleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(this);
        if (!Mouse.Capture(this, CaptureMode.Element))
        {
            return;
        }

        if (TryFindSnowballAt(position, out var snowball))
        {
            _draggedSnowball = snowball;
            _draggedSnowman = FindSnowman(snowball);
            _dragOffset = new WpfPoint(position.X - snowball.X, position.Y - snowball.Y);
            _lastDragTime = _clock.Elapsed;
            snowball.VelocityX = 0;
            snowball.VelocityY = 0;
            if (_draggedSnowman is not null)
            {
                _draggedSnowman.Bottom.VelocityX = 0;
                _draggedSnowman.Bottom.VelocityY = 0;
            }
        }
        else if (IsPointerOverGatherableSnow(position))
        {
            StartSnowGathering(position);
        }
        else
        {
            ReleaseMouseCapture();
            return;
        }

        SetClickThrough(false);
        e.Handled = true;
        InvalidateVisual();
    }

    private void HandleMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var position = e.GetPosition(this);
        if (_isGatheringSnow)
        {
            var gatherNow = _clock.Elapsed;
            var gatherElapsed = Math.Clamp((gatherNow - _lastGatherTime).TotalSeconds, 1.0 / 240.0, 0.05);
            if (GatherSnowAlongCursorPath(position, Math.Max(ActualWidth, 1), Math.Max(ActualHeight, 1), gatherElapsed))
            {
                InvalidateVisual();
            }
            _lastGatherTime = gatherNow;
            e.Handled = true;
            return;
        }

        if (_draggedSnowball is null)
        {
            return;
        }

        var previousCenter = new WpfPoint(_draggedSnowball.X, _draggedSnowball.Y);
        var desiredCenter = new WpfPoint(position.X - _dragOffset.X, position.Y - _dragOffset.Y);
        var offsetX = desiredCenter.X - _draggedSnowball.X;
        var offsetY = desiredCenter.Y - _draggedSnowball.Y;

        if (_draggedSnowman is not null)
        {
            // Clamp the whole assembly, not only the particular ball that was clicked.
            var parts = new[] { _draggedSnowman.Bottom, _draggedSnowman.Middle, _draggedSnowman.Top };
            var minX = parts.Min(part => part.X - part.Radius);
            var maxX = parts.Max(part => part.X + part.Radius);
            var minY = parts.Min(part => part.Y - part.Radius);
            var maxY = parts.Max(part => part.Y + part.Radius);
            offsetX = ClampGroupOffset(offsetX, minX, maxX, ActualWidth);
            offsetY = ClampGroupOffset(offsetY, minY, maxY, ActualHeight);
            _draggedSnowman.Translate(offsetX, offsetY);
        }
        else
        {
            var radius = _draggedSnowball.Radius;
            desiredCenter.X = Math.Clamp(desiredCenter.X, radius, Math.Max(radius, ActualWidth - radius));
            desiredCenter.Y = Math.Clamp(desiredCenter.Y, radius, Math.Max(radius, ActualHeight - radius));
            _draggedSnowball.X = desiredCenter.X;
            _draggedSnowball.Y = desiredCenter.Y;
        }

        var now = _clock.Elapsed;
        var elapsed = Math.Max((now - _lastDragTime).TotalSeconds, 1.0 / 240.0);
        var velocityX = Math.Clamp((_draggedSnowball.X - previousCenter.X) / elapsed, -900, 900);
        var velocityY = Math.Clamp((_draggedSnowball.Y - previousCenter.Y) / elapsed, -900, 900);

        if (_draggedSnowman is not null)
        {
            _draggedSnowman.Bottom.VelocityX = velocityX;
            _draggedSnowman.Bottom.VelocityY = velocityY;
            _draggedSnowman.AlignParts();
        }
        else
        {
            _draggedSnowball.VelocityX = velocityX;
            _draggedSnowball.VelocityY = velocityY;
        }

        _lastDragTime = now;
        e.Handled = true;
        InvalidateVisual();
    }

    private void HandleMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_draggedSnowball is null && !_isGatheringSnow)
        {
            UpdateClickThroughState();
        }
    }

    private void HandleMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isGatheringSnow)
        {
            FinishSnowGathering(Math.Max(ActualWidth, 1), Math.Max(ActualHeight, 1));
            e.Handled = true;
            return;
        }

        if (_draggedSnowball is null)
        {
            return;
        }

        EndDrag();
        e.Handled = true;
    }

    private void HandleLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isGatheringSnow)
        {
            FinishSnowGathering(Math.Max(ActualWidth, 1), Math.Max(ActualHeight, 1), releaseCapture: false);
        }
        else if (_draggedSnowball is not null)
        {
            EndDrag(releaseCapture: false);
        }
    }

    private void EndDrag(bool releaseCapture = true)
    {
        _draggedSnowball = null;
        _draggedSnowman = null;
        if (releaseCapture && IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        UpdateClickThroughState();
        InvalidateVisual();
    }

    private void ApplyCursorForce(Snowflake particle, double delta)
    {
        if (!_settings.CursorInteractionEnabled ||
            !_hasCursorPosition ||
            _settings.CursorMode == CursorInteractionMode.Mitten)
        {
            return;
        }

        const double influenceRadius = 88.0;
        var dx = particle.X - _cursorPosition.X;
        var dy = particle.Y - _cursorPosition.Y;
        var distanceSquared = dx * dx + dy * dy;

        if (distanceSquared >= influenceRadius * influenceRadius || distanceSquared < 0.01)
        {
            return;
        }

        var distance = Math.Sqrt(distanceSquared);
        var falloff = 1 - distance / influenceRadius;
        var force = falloff * 720.0 * delta;
        particle.VelocityX += dx / distance * force;
        particle.VelocityY += dy / distance * force;
    }

    private static void DrawSnowball(DrawingContext drawingContext, Snowball snowball)
    {
        var center = new WpfPoint(snowball.X, snowball.Y);
        var radius = snowball.Radius;

        drawingContext.DrawEllipse(
            SnowBrushes.BallShadow,
            null,
            new WpfPoint(center.X + radius * 0.08, center.Y + radius * 0.72),
            radius * 0.78,
            radius * 0.24);

        drawingContext.PushTransform(new RotateTransform(
            snowball.Rotation * 180 / Math.PI,
            center.X,
            center.Y));
        drawingContext.DrawEllipse(SnowBrushes.BallFill, SnowBrushes.BallOutline, center, radius, radius);
        drawingContext.DrawEllipse(
            SnowBrushes.BallHighlight,
            null,
            new WpfPoint(center.X - radius * 0.32, center.Y - radius * 0.38),
            radius * 0.22,
            radius * 0.12);
        drawingContext.Pop();
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

    private void SpawnImpactFragments(double x, double y, double radius)
    {
        var count = Math.Clamp((int)Math.Round(radius / 5), 4, 10);
        for (var i = 0; i < count && _particles.Count < Math.Min(900, _settings.ParticleCount + 240); i++)
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var speed = 70 + _random.NextDouble() * 210;
            _particles.Add(new Snowflake
            {
                X = x,
                Y = y,
                VelocityX = Math.Cos(angle) * speed,
                VelocityY = Math.Sin(angle) * speed - 80,
                Radius = 0.9 + _random.NextDouble() * 1.5,
                FallSpeed = 32 + _random.NextDouble() * 55,
                Drift = 5 + _random.NextDouble() * 16,
                Phase = _random.NextDouble() * Math.PI * 2,
                Opacity = 0.55 + _random.NextDouble() * 0.4
            });
        }
    }

    private void SpawnFallingSnow(double x, double y, double amount)
    {
        if (_particles.Count >= Math.Min(900, _settings.ParticleCount + 240)) return;
        _particles.Add(new Snowflake
        {
            X = x,
            Y = y,
            VelocityX = (_random.NextDouble() - 0.5) * 65,
            VelocityY = -15 - _random.NextDouble() * 35,
            Radius = Math.Clamp(0.7 + Math.Sqrt(amount) * 0.35, 0.8, 2.4),
            FallSpeed = 35 + _random.NextDouble() * 45,
            Drift = 5 + _random.NextDouble() * 15,
            Phase = _random.NextDouble() * Math.PI * 2,
            Opacity = 0.55 + _random.NextDouble() * 0.35
        });
    }

    private void ClampInteractiveObjectsToViewport(double width, double height)
    {
        foreach (var snowball in _snowballs)
        {
            var radius = snowball.Radius;
            var nextX = Math.Clamp(snowball.X, radius, Math.Max(radius, width - radius));
            var nextY = Math.Clamp(snowball.Y, radius, Math.Max(radius, height - radius));
            if (Math.Abs(nextX - snowball.X) > 0.01) snowball.VelocityX = 0;
            if (Math.Abs(nextY - snowball.Y) > 0.01) snowball.VelocityY = 0;
            snowball.X = nextX;
            snowball.Y = nextY;
        }

        foreach (var snowman in _snowmen)
        {
            var parts = new[] { snowman.Bottom, snowman.Middle, snowman.Top };
            var minX = parts.Min(part => part.X - part.Radius);
            var maxX = parts.Max(part => part.X + part.Radius);
            var minY = parts.Min(part => part.Y - part.Radius);
            var maxY = parts.Max(part => part.Y + part.Radius);
            var offsetX = ClampGroupOffset(0, minX, maxX, width);
            var offsetY = ClampGroupOffset(0, minY, maxY, height);
            snowman.Translate(offsetX, offsetY);
            if (Math.Abs(offsetX) > 0.01) snowman.Bottom.VelocityX = 0;
            if (Math.Abs(offsetY) > 0.01) snowman.Bottom.VelocityY = 0;
            snowman.AlignParts();
        }
    }

    private void ResetParticles()
    {
        if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        _snowSurface.Resize(ActualWidth, ActualHeight);
        ClampInteractiveObjectsToViewport(ActualWidth, ActualHeight);
        _particles.Clear();
        for (var i = 0; _snowfallState.IsGenerating && i < _settings.ParticleCount; i++)
        {
            _particles.Add(CreateParticle(randomY: true));
        }

        _lastFrame = _clock.Elapsed;
        InvalidateVisual();
    }

    private void HandleViewportSizeChanged()
    {
        if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var width = ActualWidth;
        var height = ActualHeight;
        _snowSurface.Resize(width, height);
        ClampInteractiveObjectsToViewport(width, height);
        foreach (var particle in _particles)
        {
            particle.X = Math.Clamp(particle.X, -particle.Radius, width + particle.Radius);
            particle.Y = Math.Clamp(particle.Y, -particle.Radius, height + particle.Radius);
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

    public void ClearSnowballs()
    {
        _draggedSnowball = null;
        _draggedSnowman = null;
        _snowballs.Clear();
        _snowmen.Clear();

        if (_isGatheringSnow)
        {
            _isGatheringSnow = false;
            _formingSnowball = null;
            _gatheredSnowArea = 0;
        }

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        UpdateClickThroughState();
        InvalidateVisual();
    }

    public void ClearAllSnow()
    {
        if (_isGatheringSnow)
        {
            _isGatheringSnow = false;
            _formingSnowball = null;
            _gatheredSnowArea = 0;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
        }

        _draggedSnowball = null;
        _draggedSnowman = null;
        _snowballs.Clear();
        _snowmen.Clear();
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        _snowSurface.ClearImmediately();
        _windowSnowSurface.Clear();
        var height = Math.Max(ActualHeight, 1);
        foreach (var particle in _particles)
        {
            particle.Y = -particle.Radius - _random.NextDouble() * height;
            particle.VelocityX = 0;
            particle.VelocityY = 0;
        }

        UpdateClickThroughState();
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
        public static readonly SolidColorBrush BallFill = Create(248, 252, 255, 250);
        public static readonly SolidColorBrush BallHighlight = Create(255, 255, 255, 195);
        public static readonly SolidColorBrush BallShadow = Create(90, 120, 148, 55);
        public static readonly WpfPen BallOutline = CreatePen(180, 211, 235, 225, 1.4);

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
