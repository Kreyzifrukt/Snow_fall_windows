using System.Runtime.InteropServices;
using WpfPoint = System.Windows.Point;

namespace Flake.Interop;

/// <summary>Reads the global pointer position without making the desktop overlay interactive.</summary>
internal static class CursorInput
{
    public static bool TryGetPosition(out WpfPoint position)
    {
        if (!GetCursorPos(out var nativePoint))
        {
            position = default;
            return false;
        }

        position = new WpfPoint(nativePoint.X, nativePoint.Y);
        return true;
    }

    /// <summary>Reads the physical left button state without capturing or blocking desktop input.</summary>
    public static bool IsLeftButtonDown() => (GetAsyncKeyState(VkLButton) & 0x8000) != 0;

    private const int VkLButton = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
