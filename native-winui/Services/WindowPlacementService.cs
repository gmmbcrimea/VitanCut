using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace VitanCut.WinUI.Services;

internal sealed record WindowSessionState(int Left, int Top, int Right, int Bottom, bool Maximized)
{
    public bool IsValid => (long)Right - Left is > 0 and <= 32768 && (long)Bottom - Top is > 0 and <= 32768;
}

internal sealed class WindowPlacementService(string path)
{
    internal WindowSessionState? Load()
    {
        try
        {
            var state = JsonSerializer.Deserialize<WindowSessionState>(File.ReadAllText(path));
            return state is { IsValid: true } ? state : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    internal void Save(WindowSessionState state)
    {
        if (!state.IsValid) return;
        try { AtomicJsonFile.Write(path, JsonSerializer.Serialize(state)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Window placement could not be saved: {error.Message}");
        }
    }

    public void Capture(IntPtr window)
    {
        var placement = new Placement { Length = Marshal.SizeOf<Placement>() };
        if (!GetWindowPlacement(window, ref placement)) return;
        var bounds = placement.NormalPosition;
        // A minimized window may still need to return to its maximized state.
        Save(new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom,
            placement.ShowCommand == 3 || (placement.ShowCommand == 2 && (placement.Flags & 2) != 0)));
    }

    public void Restore(IntPtr window)
    {
        if (Load() is not { } state) return;
        // Windows restores workspace coordinates and relocates off-screen windows
        // to an available monitor when the display configuration has changed.
        var placement = new Placement
        {
            Length = Marshal.SizeOf<Placement>(),
            ShowCommand = state.Maximized ? 3 : 1,
            NormalPosition = new Rect { Left = state.Left, Top = state.Top, Right = state.Right, Bottom = state.Bottom }
        };
        SetWindowPlacement(window, ref placement);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Placement
    {
        public int Length, Flags, ShowCommand;
        public Point MinPosition, MaxPosition;
        public Rect NormalPosition;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr window, ref Placement placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(IntPtr window, ref Placement placement);
}
