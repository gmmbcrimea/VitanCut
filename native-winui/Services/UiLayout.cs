namespace VitanCut.WinUI.Services;

internal static class UiLayout
{
    public static bool ContainsPoint(double x, double y, double width, double height) =>
        x >= 0 && y >= 0 && x < width && y < height;

    public static double CompactGap(double original) => original <= 0 ? 0 : Math.Min(8, original * .4);

    public static double CompactInset(double original) => original <= 0 ? 0 : Math.Min(12, original * .5);

    public static bool ReportIsNarrow(double width) => width < 800;

    public static bool ReportImageBesideTable(double width) => width >= 1000;

    public static double WorkListHeight(double availableHeight, double headerHeight) =>
        Math.Clamp(availableHeight - headerHeight - 112, 360, 720);

    public static double MaterialColumnWidth(double requested, double available, double other, bool categories)
    {
        var minimum = categories ? 150 : 220;
        var maximum = categories ? 360 : 520;
        var budget = Math.Max(minimum, available - other - 320 - 24);
        return Math.Clamp(requested, minimum, Math.Min(maximum, budget));
    }
}
