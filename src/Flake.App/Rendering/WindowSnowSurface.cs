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
    private const int DwmwaExtendedFrameBounds = 9;
    private const int DwmwaCloaked = 14;
    private readonly Dictionary<IntPtr, WindowSnowPatch> _patches = [];
    private List<WindowSnowPatch> _orderedPatches = [];

    public event Action<double, double, double>? SnowDropped;

    public static bool IsFullscreenForegroundWindow(int ownProcessId)
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero || !IsWindowVisible(handle))
        {
            return false;
        }

        GetWindowThreadProcessId(handle, out var processId);
        if (processId == (uint)ownProcessId || !TryGetVisibleBounds(handle, out var windowBounds))
        {
            return false;
        }

        var monitor = MonitorFromWindow(handle, 2);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        var bounds = monitorInfo.Monitor;
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        return width > 0 && height > 0 &&
            windowBounds.Left <= bounds.Left + 2 && windowBounds.Top <= bounds.Top + 2 &&
            windowBounds.Right >= bounds.Right - 2 && windowBounds.Bottom >= bounds.Bottom - 2;
    }

    public void Refresh(Func<WpfPoint, WpfPoint> screenToLocal, int ownProcessId, double width, double height)
    {
        var ordered = new List<WindowSnowPatch>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || IsIconic(handle) || IsCloaked(handle) ||
                GetWindow(handle, OwnerWindow) != IntPtr.Zero ||
                IsDesktopHostWindow(handle))
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);
            if (processId == (uint)ownProcessId || !TryGetVisibleBounds(handle, out var nativeRect))
            {
                return true;
            }

            var screenBounds = new Rect(
                nativeRect.Left,
                nativeRect.Top,
                nativeRect.Right - nativeRect.Left,
                nativeRect.Bottom - nativeRect.Top);
            var bounds = ConvertScreenBounds(screenBounds, screenToLocal);
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

    internal static Rect ConvertScreenBounds(Rect screenBounds, Func<WpfPoint, WpfPoint> screenToLocal)
    {
        var topLeft = screenToLocal(screenBounds.TopLeft);
        var bottomRight = screenToLocal(screenBounds.BottomRight);
        return new Rect(topLeft, bottomRight);
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

    public bool TryLandFlake(double oldX, double oldBottom, double newX, double newBottom, double radius, double viewportWidth)
    {
        if (viewportWidth > 0 && Math.Abs(newX - oldX) > viewportWidth * 0.5)
        {
            return false;
        }

        foreach (var patch in _orderedPatches)
        {
            if (patch.TryLandFlake(oldX, oldBottom, newX, newBottom, radius)) return true;
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
        // EnumWindows returns front-to-back; draw back-to-front so foreground edges stay visible.
        for (var index = _orderedPatches.Count - 1; index >= 0; index--)
        {
            _orderedPatches[index].Draw(context);
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

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private static bool IsCloaked(IntPtr handle)
    {
        int cloaked;
        return DwmGetWindowAttribute(handle, DwmwaCloaked, out cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    private static bool TryGetVisibleBounds(IntPtr handle, out NativeRect bounds)
    {
        if (DwmGetWindowAttribute(handle, DwmwaExtendedFrameBounds, out bounds, Marshal.SizeOf<NativeRect>()) == 0 &&
            bounds.Right > bounds.Left && bounds.Bottom > bounds.Top)
        {
            return true;
        }

        return GetWindowRect(handle, out bounds);
    }

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out NativeRect value, int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int valueSize);

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
    private const double SampleSpacing = 8;
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
        if (Math.Abs(bounds.Left - previous.Left) > 0.5 || Math.Abs(bounds.Top - previous.Top) > 0.5)
        {
            // Snow is attached to the old physical surface. Once the window moves it must fall.
            ReleaseAllSnow();
        }

        if (Math.Abs(bounds.Width - previous.Width) > 0.5)
        {
            _top = Resample(_top, Math.Max(2, (int)Math.Ceiling(bounds.Width / SampleSpacing) + 1));
        }

        if (Math.Abs(bounds.Height - previous.Height) > 0.5)
        {
            var count = Math.Max(2, (int)Math.Ceiling(bounds.Height / SampleSpacing) + 1);
            _left = Resample(_left, count);
            _right = Resample(_right, count);
        }

        Bounds = bounds;
    }

    public double GetTopHeight(double x) => Sample(_top, x - Bounds.Left, Bounds.Width);
    public double GetSideHeight(double y, bool left) => Sample(left ? _left : _right, y - Bounds.Top, Bounds.Height);

    public void DepositTop(double x, double amount) => Deposit(_top, x - Bounds.Left, Bounds.Width, amount);
    public void DepositSide(double y, double amount, bool left) => Deposit(left ? _left : _right, y - Bounds.Top, Bounds.Height, amount);

    public bool TryLandFlake(double oldX, double oldBottom, double newX, double newBottom, double radius)
    {
        var span = Math.Max(Math.Abs(newX - oldX), Math.Abs(newBottom - oldBottom));
        var steps = Math.Clamp((int)Math.Ceiling(span / SampleSpacing), 1, 64);
        var previousGap = double.NegativeInfinity;
        for (var step = 0; step <= steps; step++)
        {
            var progress = (double)step / steps;
            var x = oldX + (newX - oldX) * progress;
            if (x < Bounds.Left || x > Bounds.Right)
            {
                previousGap = double.NegativeInfinity;
                continue;
            }

            var bottom = oldBottom + (newBottom - oldBottom) * progress;
            var surfaceY = Bounds.Top - GetTopHeight(x);
            var gap = bottom - surfaceY;
            if (previousGap <= 0 && gap >= 0)
            {
                DepositTop(x, Math.Max(0.45, radius * 0.22));
                return true;
            }
            previousGap = gap;
        }

        return false;
    }

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
