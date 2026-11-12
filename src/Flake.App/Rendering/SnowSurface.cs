using System.Windows.Media;
using WpfPoint = System.Windows.Point;
using WpfColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;

namespace Flake.Rendering;

/// <summary>Stores and draws the snow layer accumulated along the bottom of the overlay.</summary>
internal sealed class SnowSurface
{
    private const double TargetSampleSpacing = 6.0;
    private const double MaximumDepthRatio = 0.38;
    private const double MaximumDepthPixels = 420.0;

    private static readonly SolidColorBrush SnowBrush = CreateBrush(246, 250, 255, 242);
    private static readonly WpfPen EdgePen = CreateEdgePen();

    private double[] _heights = [];
    private double _width;
    private double _height;
    private double _sampleSpacing;
    private StreamGeometry? _fillGeometry;
    private StreamGeometry? _edgeGeometry;
    private bool _geometryDirty = true;
    private bool _hasSnow;

    public void Resize(double width, double height)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);

        if (Math.Abs(width - _width) < 0.1 && Math.Abs(height - _height) < 0.1)
        {
            return;
        }

        var oldHeights = _heights;
        var oldWidth = _width;
        var sampleCount = Math.Max(2, (int)Math.Ceiling(width / TargetSampleSpacing) + 1);

        _width = width;
        _height = height;
        _sampleSpacing = width / (sampleCount - 1);
        _heights = new double[sampleCount];
        _hasSnow = false;

        if (oldHeights.Length > 0 && oldWidth > 0)
        {
            for (var i = 0; i < _heights.Length; i++)
            {
                var newX = i * _sampleSpacing;
                var oldPosition = Math.Clamp(newX / oldWidth * (oldHeights.Length - 1), 0, oldHeights.Length - 1);
                var leftIndex = (int)Math.Floor(oldPosition);
                var rightIndex = Math.Min(leftIndex + 1, oldHeights.Length - 1);
                var blend = oldPosition - leftIndex;
                var oldHeight = oldHeights[leftIndex] + (oldHeights[rightIndex] - oldHeights[leftIndex]) * blend;
                _heights[i] = Math.Min(oldHeight, MaximumDepth);
                _hasSnow |= _heights[i] > 0.001;
            }
        }

        _geometryDirty = true;
    }

    public double GetHeightAt(double x)
    {
        if (_heights.Length < 2 || _sampleSpacing <= 0)
        {
            return 0;
        }

        var position = Math.Clamp(x, 0, _width) / _sampleSpacing;
        var leftIndex = Math.Min((int)Math.Floor(position), _heights.Length - 1);
        var rightIndex = Math.Min(leftIndex + 1, _heights.Length - 1);
        var blend = position - leftIndex;
        return _heights[leftIndex] + (_heights[rightIndex] - _heights[leftIndex]) * blend;
    }

    public void Accumulate(double x, double particleRadius)
    {
        if (_heights.Length < 2 || _sampleSpacing <= 0)
        {
            return;
        }

        var center = Math.Clamp(x, 0, _width);
        var centerIndex = (int)Math.Round(center / _sampleSpacing);
        var spread = Math.Max(12, particleRadius * 7 + 4);
        var sigma = spread * 0.5;
        var affectedSamples = (int)Math.Ceiling(spread / _sampleSpacing);
        var depositAmount = Math.Clamp(0.65 + particleRadius * 0.18, 0.55, 1.20);
        var changed = false;

        for (var index = Math.Max(0, centerIndex - affectedSamples);
             index <= Math.Min(_heights.Length - 1, centerIndex + affectedSamples);
             index++)
        {
            var distance = (index - centerIndex) * _sampleSpacing;
            var weight = Math.Exp(-(distance * distance) / (2 * sigma * sigma));
            var increase = depositAmount * weight;
            var nextHeight = Math.Min(_heights[index] + increase, MaximumDepth);

            if (nextHeight > _heights[index])
            {
                _heights[index] = nextHeight;
                changed = true;
            }
        }

        if (changed)
        {
            _hasSnow = true;
            _geometryDirty = true;
        }
    }

    public void Clear()
    {
        if (_heights.Length == 0)
        {
            return;
        }

        Array.Clear(_heights, 0, _heights.Length);
        _hasSnow = false;
        _geometryDirty = true;
    }

    public void Draw(DrawingContext drawingContext)
    {
        if (!_hasSnow || _heights.Length < 2)
        {
            return;
        }

        if (_geometryDirty || _fillGeometry is null || _edgeGeometry is null)
        {
            BuildGeometry();
        }

        drawingContext.DrawGeometry(SnowBrush, null, _fillGeometry);
        drawingContext.DrawGeometry(null, EdgePen, _edgeGeometry);
    }

    private double MaximumDepth => Math.Min(_height * MaximumDepthRatio, MaximumDepthPixels);

    private void BuildGeometry()
    {
        var fillGeometry = new StreamGeometry();
        using (var context = fillGeometry.Open())
        {
            context.BeginFigure(new WpfPoint(0, _height), isFilled: true, isClosed: true);
            context.LineTo(new WpfPoint(0, _height - _heights[0]), isStroked: true, isSmoothJoin: true);

            for (var i = 1; i < _heights.Length; i++)
            {
                var x = Math.Min(i * _sampleSpacing, _width);
                context.LineTo(new WpfPoint(x, _height - _heights[i]), isStroked: true, isSmoothJoin: true);
            }

            context.LineTo(new WpfPoint(_width, _height), isStroked: true, isSmoothJoin: true);
        }

        fillGeometry.Freeze();

        var edgeGeometry = new StreamGeometry();
        using (var context = edgeGeometry.Open())
        {
            context.BeginFigure(new WpfPoint(0, _height - _heights[0]), isFilled: false, isClosed: false);
            for (var i = 1; i < _heights.Length; i++)
            {
                var x = Math.Min(i * _sampleSpacing, _width);
                context.LineTo(new WpfPoint(x, _height - _heights[i]), isStroked: true, isSmoothJoin: true);
            }
        }

        edgeGeometry.Freeze();
        _fillGeometry = fillGeometry;
        _edgeGeometry = edgeGeometry;
        _geometryDirty = false;
    }

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue, byte alpha)
    {
        var brush = new SolidColorBrush(WpfColor.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static WpfPen CreateEdgePen()
    {
        var pen = new WpfPen(CreateBrush(205, 226, 246, 190), 1.2);
        pen.Freeze();
        return pen;
    }
}
