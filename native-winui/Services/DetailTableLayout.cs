namespace VitanCut.WinUI.Services;

internal static class DetailTableLayout
{
    public const double MinimumWidth = 998;

    public static double[] ColumnWidths(double availableWidth)
    {
        // Header and rows use the same pixel tracks, independent of text measurement.
        var extra = Math.Max(0, availableWidth - MinimumWidth) / 6;
        return [180 + 2 * extra, 180 + 2 * extra, 72, 150 + extra, 150 + extra, 56, 150];
    }
}
