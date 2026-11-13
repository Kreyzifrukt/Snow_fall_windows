using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using WpfPoint = System.Windows.Point;
using WpfColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;

namespace Flake.Rendering;

internal enum WindowSnowEdge
{
    Top,
    Left,
    Right
}

internal readonly record struct WindowSnowHit(
    WindowSnowPatch Patch,
    WindowSnowEdge Edge,
    double Position,
    double Surface);

/// <summary>
/// Tracks visible top-level application windows and draws a thin snow layer on their
/// top and side edges. It never changes the native windows or their client areas.
/// </summary>
internal sealed class WindowSnowSurface
{
    private const double SampleSpacing = 8;
    private const uint OwnerWindow = 4;
    private readonly Dictionary<IntPtr, WindowSnowPatch> _patches = [];
    private List<WindowSnowPatch> _orderedPatches = [];

    public event Action<double, double, double>? SnowDropped;

    public void Refresh(Func<WpfPoint, WpfPoint> screenToLocal, int ownProcessId, double width, double height)
    {
        var ordered = new List<WindowSnowPatch>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || IsIconic(handle) ||
                GetWindow(handle, OwnerWindow) != IntPtr.Zero ||
                IsDesktopHostWindow(handle))
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);
            if (processId == (uint)ownProcessId || !GetWindowRect(handle, out var nativeRect))
            {
                return true;
            }

            var topLeft = screenToLocal(new WpfPoint(nativeRect.Left, nativeRect.Top));
            var bottomRight = screenToLocal(new WpfPoint(nativeRect.Right, nativeRect.Bottom));
            var bounds = new Rect(topLeft, bottomRight);
            if (bounds.Width < 150 || bounds.Height < 90 ||
                bounds.Right < 0 || bounds.Bottom < 0 || bounds.Left > width || bounds.Top > height)
            {
                return true;
            }

            if (!_patches.TryGetValue(handle, out var patch))
            {
                patch = new WindowSnowPatch(handle, bounds);
                patch.SnowDropped += (x, y, amount) => SnowDropped?.Invoke(x, y, amount);
                _patches.Add(handle, patch);
            }
            else
            {
                patch.UpdateBounds(bounds);
            }

            ordered.Add(patch);
            return ordered.Count < 120;
        }, IntPtr.Zero);

        var activeHandles = ordered.Select(patch => patch.Handle).ToHashSet();
        foreach (var staleHandle in _patches.Keys.Where(handle => !activeHandles.Contains(handle)).ToArray())
        {
            _patches[staleHandle].ReleaseAllSnow();
            _patches.Remove(staleHandle);
        }

        _orderedPatches = ordered;
    }

    public bool TryGetTopSurfaceAt(double x, double centerY, double radius, out double surfaceY)
    {
        foreach (var patch in _orderedPatches)
        {
            if (x < patch.Bounds.Left || x > patch.Bounds.Right) continue;
            var candidate = patch.Bounds.Top - patch.GetTopHeight(x);
            if (Math.Abs(centerY + radius - candidate) <= 7)
            {
                surfaceY = candidate;
                return true;
            }
        }
        surfaceY = 0;
        return false;
    }

    public bool TryLandFlake(double x, double oldBottom, double newBottom, double radius)
    {
        foreach (var patch in _orderedPatches)
        {
            if (x < patch.Bounds.Left || x > patch.Bounds.Right)
            {
                continue;
            }

            var surfaceY = patch.Bounds.Top - patch.GetTopHeight(x);
            if (oldBottom <= surfaceY && newBottom >= surfaceY)
            {
                patch.DepositTop(x, Math.Max(0.45, radius * 0.22));
                return true;
            }
        }

        return false;
    }

    public bool TryHitBallEdge(
        double oldX, double oldY, double newX, double newY, double radius, out WindowSnowHit hit)
    {
        foreach (var patch in _orderedPatches)
        {
            if (newX >= patch.Bounds.Left && newX <= patch.Bounds.Right &&
                oldY + radius <= patch.Bounds.Top - patch.GetTopHeight(newX) &&
                newY + radius >= patch.Bounds.Top - patch.GetTopHeight(newX) &&
                oldY < patch.Bounds.Top)
            {
                hit = new WindowSnowHit(
                    patch, WindowSnowEdge.Top, newX,
                    patch.Bounds.Top - patch.GetTopHeight(newX));
                return true;
            }

            var centerY = newY;
            if (centerY < patch.Bounds.Top || centerY > patch.Bounds.Bottom)
            {
                continue;
            }

            var leftSurface = patch.Bounds.Left - patch.GetSideHeight(centerY, left: true);
            if (oldX + radius <= leftSurface && newX + radius >= leftSurface)
            {
                hit = new WindowSnowHit(patch, WindowSnowEdge.Left, centerY, leftSurface);
                return true;
            }

            var rightSurface = patch.Bounds.Right + patch.GetSideHeight(centerY, left: false);
            if (oldX - radius >= rightSurface && newX - radius <= rightSurface)
            {
                hit = new WindowSnowHit(patch, WindowSnowEdge.Right, centerY, rightSurface);
                return true;
            }
        }

        hit = default;
        return false;
    }

    public void DepositImpact(WindowSnowHit hit, double amount)
    {
        if (hit.Edge == WindowSnowEdge.Top)
        {
            hit.Patch.DepositTop(hit.Position, amount);
        }
        else
        {
            hit.Patch.DepositSide(hit.Position, amount, hit.Edge == WindowSnowEdge.Left);
        }
    }

    public void Draw(DrawingContext context)
    {
        foreach (var patch in _orderedPatches)
        {
            patch.Draw(context);
        }
    }

    public void Clear()
    {
        foreach (var patch in _patches.Values)
        {
            patch.Clear();
        }
    }

    private static bool IsDesktopHostWindow(IntPtr handle)
    {
        var buffer = new System.Text.StringBuilder(128);
        _ = GetClassName(handle, buffer, buffer.Capacity);
        return buffer.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, System.Text.StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr handle, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

internal sealed class WindowSnowPatch
{
    private static readonly SolidColorBrush SnowBrush = CreateBrush();
    private static readonly WpfPen EdgePen = CreatePen();
    private double[] _top = [];
    private double[] _left = [];
    private double[] _right = [];

    public IntPtr Handle { get; }
    public Rect Bounds { get; private set; }
    public event Action<double, double, double>? SnowDropped;

    public WindowSnowPatch(IntPtr handle, Rect bounds)
    {
        Handle = handle;
        Bounds = bounds;
        ResizeProfiles(bounds.Width, bounds.Height);
    }

    public void UpdateBounds(Rect bounds)
    {
        var previous = Bounds;
        if (Math.Abs(bounds.Width - previous.Width) > 0.5)
        {
            var oldTop = _top;
            var nextTop = new double[Math.Max(2, (int)Math.Ceiling(bounds.Width / SampleSpacing) + 1)];
            for (var i = 0; i < oldTop.Length; i++)
            {
                var oldX = previous.Left + i * previous.Width / Math.Max(1, oldTop.Length - 1);
                if (oldX < bounds.Left || oldX > bounds.Right)
                {
                    if (oldTop[i] > 0.25) SnowDropped?.Invoke(oldX, previous.Top - oldTop[i], oldTop[i]);
                    continue;
                }
                var target = (int)Math.Round((oldX - bounds.Left) / Math.Max(1, bounds.Width) * (nextTop.Length - 1));
                nextTop[Math.Clamp(target, 0, nextTop.Length - 1)] = Math.Max(nextTop[Math.Clamp(target, 0, nextTop.Length - 1)], oldTop[i]);
            }
            _top = nextTop;
        }

        if (Math.Abs(bounds.Height - previous.Height) > 0.5)
        {
            var oldLeft = _left;
            var oldRight = _right;
            var count = Math.Max(2, (int)Math.Ceiling(bounds.Height / SampleSpacing) + 1);
            var nextLeft = new double[count];
            var nextRight = new double[count];
            for (var i = 0; i < oldLeft.Length; i++)
            {
                var oldY = previous.Top + i * previous.Height / Math.Max(1, oldLeft.Length - 1);
                if (oldY < bounds.Top || oldY > bounds.Bottom)
                {
                    if (oldLeft[i] > 0.25) SnowDropped?.Invoke(previous.Left - oldLeft[i], oldY, oldLeft[i]);
                    if (oldRight[i] > 0.25) SnowDropped?.Invoke(previous.Right + oldRight[i], oldY, oldRight[i]);
                    continue;
                }
                var target = (int)Math.Round((oldY - bounds.Top) / Math.Max(1, bounds.Height) * (count - 1));
                target = Math.Clamp(target, 0, count - 1);
                nextLeft[target] = Math.Max(nextLeft[target], oldLeft[i]);
                nextRight[target] = Math.Max(nextRight[target], oldRight[i]);
            }
            _left = nextLeft;
            _right = nextRight;
        }

        Bounds = bounds;
    }

    public double GetTopHeight(double x) => Sample(_top, x - Bounds.Left, Bounds.Width);
    public double GetSideHeight(double y, bool left) => Sample(left ? _left : _right, y - Bounds.Top, Bounds.Height);

    public void DepositTop(double x, double amount) => Deposit(_top, x - Bounds.Left, Bounds.Width, amount);
    public void DepositSide(double y, double amount, bool left) => Deposit(left ? _left : _right, y - Bounds.Top, Bounds.Height, amount);

    public void Draw(DrawingContext context)
    {
        var topStep = Bounds.Width / Math.Max(1, _top.Length - 1);
        for (var i = 0; i < _top.Length; i++)
        {
            var depth = _top[i];
            if (depth <= 0.15) continue;
            var x = Bounds.Left + i * topStep;
            context.DrawRectangle(SnowBrush, null,
                new Rect(x, Bounds.Top - depth, Math.Max(1, topStep + 0.5), depth));
        }

        var sideStep = Bounds.Height / Math.Max(1, _left.Length - 1);
        for (var i = 0; i < _left.Length; i++)
        {
            var y = Bounds.Top + i * sideStep;
            if (_left[i] > 0.15)
                context.DrawRectangle(SnowBrush, null,
                    new Rect(Bounds.Left - _left[i], y, _left[i], Math.Max(1, sideStep + 0.5)));
            if (_right[i] > 0.15)
                context.DrawRectangle(SnowBrush, null,
                    new Rect(Bounds.Right, y, _right[i], Math.Max(1, sideStep + 0.5)));
        }
    }

    public void Clear()
    {
        Array.Clear(_top, 0, _top.Length);
        Array.Clear(_left, 0, _left.Length);
        Array.Clear(_right, 0, _right.Length);
    }

    public void ReleaseAllSnow()
    {
        var topStep = Bounds.Width / Math.Max(1, _top.Length - 1);
        for (var i = 0; i < _top.Length; i++)
        {
            if (_top[i] > 0.25) SnowDropped?.Invoke(Bounds.Left + i * topStep, Bounds.Top - _top[i], _top[i]);
        }

        var sideStep = Bounds.Height / Math.Max(1, _left.Length - 1);
        for (var i = 0; i < _left.Length; i++)
        {
            var y = Bounds.Top + i * sideStep;
            if (_left[i] > 0.25) SnowDropped?.Invoke(Bounds.Left - _left[i], y, _left[i]);
            if (_right[i] > 0.25) SnowDropped?.Invoke(Bounds.Right + _right[i], y, _right[i]);
        }
        Clear();
    }

    private void ResizeProfiles(double width, double height)
    {
        _top = new double[Math.Max(2, (int)Math.Ceiling(width / SampleSpacing) + 1)];
        var sideCount = Math.Max(2, (int)Math.Ceiling(height / SampleSpacing) + 1);
        _left = new double[sideCount];
        _right = new double[sideCount];
    }

    private static double[] Resample(double[] source, int count)
    {
        if (source.Length == 0) return new double[count];
        var result = new double[count];
        for (var i = 0; i < count; i++)
        {
            var position = (double)i / Math.Max(1, count - 1) * (source.Length - 1);
            var first = (int)Math.Floor(position);
            var second = Math.Min(first + 1, source.Length - 1);
            result[i] = source[first] + (source[second] - source[first]) * (position - first);
        }
        return result;
    }

    private static double Sample(double[] profile, double coordinate, double length)
    {
        if (profile.Length < 2 || length <= 0) return 0;
        var position = Math.Clamp(coordinate / length, 0, 1) * (profile.Length - 1);
        var first = (int)Math.Floor(position);
        var second = Math.Min(first + 1, profile.Length - 1);
        return profile[first] + (profile[second] - profile[first]) * (position - first);
    }

    private static void Deposit(double[] profile, double coordinate, double length, double amount)
    {
        if (profile.Length < 2 || length <= 0 || amount <= 0) return;
        var center = Math.Clamp(coordinate / length, 0, 1) * (profile.Length - 1);
        var centerIndex = (int)Math.Round(center);
        const double sigma = 1.6;
        for (var i = Math.Max(0, centerIndex - 5); i <= Math.Min(profile.Length - 1, centerIndex + 5); i++)
        {
            var distance = i - center;
            profile[i] = Math.Min(24, profile[i] + amount * Math.Exp(-(distance * distance) / (2 * sigma * sigma)));
        }
    }

    private static SolidColorBrush CreateBrush()
    {
        var brush = new SolidColorBrush(WpfColor.FromArgb(238, 246, 251, 255));
        brush.Freeze();
        return brush;
    }

    private static WpfPen CreatePen()
    {
        var pen = new WpfPen(new SolidColorBrush(WpfColor.FromArgb(180, 205, 226, 246)), 0.8);
        pen.Freeze();
        return pen;
    }
}
