namespace VitanCut.WinUI.Services;

internal static class MaterialPickerLayout
{
    internal const double DefaultHeight = 816;
    internal const double RowHeight = 48;
    private const double Width = 356;
    private const double Margin = 8;

    internal static double ContentHeight(int rowCount, double chromeHeight) =>
        Math.Max(0, chromeHeight) + rowCount * RowHeight;

    internal static (double Width, double Height, double Left, double Top) Place(
        double windowWidth, double windowHeight, double x, double y, double desiredHeight)
    {
        var width = Math.Min(Width, Math.Max(0, windowWidth - 2 * Margin));
        var height = Math.Min(Math.Max(DefaultHeight, desiredHeight), Math.Max(0, windowHeight - 2 * Margin));
        return (width, height, Anchor(x, width, windowWidth), Anchor(y, height, windowHeight));
    }

    private static double Anchor(double cursor, double size, double windowSize)
    {
        var last = Math.Max(Margin, windowSize - size - Margin);
        var after = cursor + Margin;
        var before = cursor - Margin - size;
        if (after >= Margin && after <= last) return after;
        if (before >= Margin && before <= last) return before;

        // On a fully occupied axis there is no room to clear the cursor; keep the panel in view.
        return Math.Clamp(cursor <= windowSize / 2 ? after : before, Margin, last);
    }
}
