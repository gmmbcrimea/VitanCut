using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace VitanCut.WinUI.Controllers;

/// <summary>
/// Makes child windows owned by the main window and reliably brings them to the foreground.
/// </summary>
public sealed class OwnedWindowActivator(Window owner)
{
    private const int GwlHwndParent = -8;

    public void Activate(Window window)
    {
        var child = WindowNative.GetWindowHandle(window);
        SetWindowLongPtr(child, GwlHwndParent, WindowNative.GetWindowHandle(owner));
        window.Activate();
        window.DispatcherQueue.TryEnqueue(() =>
        {
            window.Activate();
            ShowWindow(child, 5);
            BringWindowToTop(child);
            SetForegroundWindow(child);
        });
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newLong);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
}