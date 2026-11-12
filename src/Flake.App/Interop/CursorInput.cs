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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
