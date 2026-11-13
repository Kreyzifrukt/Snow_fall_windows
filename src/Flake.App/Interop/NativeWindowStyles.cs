using System.Runtime.InteropServices;

namespace Flake.Interop;

internal static class NativeWindowStyles
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    public static void MakeOverlayClickThrough(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var extendedStyle = GetWindowLong(handle, GwlExStyle);
        extendedStyle |= WsExToolWindow | WsExNoActivate | WsExTransparent;
        SetWindowLong(handle, GwlExStyle, extendedStyle);
        RefreshWindowFrame(handle);
    }

    /// <summary>
    /// Enables mouse input only while the pointer is over an interactive snow object.
    /// The non-activating and tool-window styles are preserved.
    /// </summary>
    public static void SetClickThrough(IntPtr handle, bool clickThrough)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var extendedStyle = GetWindowLong(handle, GwlExStyle);
        var updatedStyle = clickThrough
            ? extendedStyle | WsExTransparent
            : extendedStyle & ~WsExTransparent;

        if (updatedStyle == extendedStyle)
        {
            return;
        }

        SetWindowLong(handle, GwlExStyle, updatedStyle);
        RefreshWindowFrame(handle);
    }

    private static void RefreshWindowFrame(IntPtr handle)
    {
        SetWindowPos(
            handle,
            HwndTopmost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
