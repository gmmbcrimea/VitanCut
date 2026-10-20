using System.Runtime.InteropServices;
using WinRT.Interop;

namespace VitanCut.WinUI.Views;

public sealed partial class StartupWindow
{
    private void RemoveNativeFrame()
    {
        var handle = WindowNative.GetWindowHandle(this);
        const int styleIndex = -16;
        const int extendedStyleIndex = -20;
        const long frameStyles = 0x00C40000; // Caption, border and sizing frame.
        const long edgeStyles = 0x00020301; // Dialog, window, client and static edges.
        SetWindowLongPtr(handle, styleIndex, new IntPtr(GetWindowLongPtr(handle, styleIndex).ToInt64() & ~frameStyles));
        SetWindowLongPtr(handle, extendedStyleIndex, new IntPtr(GetWindowLongPtr(handle, extendedStyleIndex).ToInt64() & ~edgeStyles));
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0037);

        var roundCorners = 2;
        DwmSetWindowAttribute(handle, 33, ref roundCorners, sizeof(int));
        var noBorder = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(handle, 34, ref noBorder, sizeof(int));
        UpdateRoundedRegion();
    }

    private void UpdateRoundedRegion()
    {
        var handle = WindowNative.GetWindowHandle(this);
        if (!GetWindowRect(handle, out var bounds)) return;
        var dpi = GetDpiForWindow(handle);
        var diameter = (int)Math.Round(24 * (dpi == 0 ? 96 : dpi) / 96d);
        // A native region also clips Acrylic on borderless Windows 10 windows.
        var region = CreateRoundRectRgn(0, 0, bounds.Right - bounds.Left + 1, bounds.Bottom - bounds.Top + 1, diameter, diameter);
        if (region != IntPtr.Zero && SetWindowRgn(handle, region, true) == 0) DeleteObject(region);
        // On success, Windows owns the region and releases it when replaced or closed.
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowBounds { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out WindowBounds bounds);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
