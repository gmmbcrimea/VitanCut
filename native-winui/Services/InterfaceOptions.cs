using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

internal static class InterfaceOptions
{
    internal static string Scale(string? scale) => scale is "compact" or "large" ? scale : "normal";
    internal static double Speed(double speed) => speed is .25 or .5 or 2 ? speed : 1;
    internal static double Radius(double radius) => radius is .5 or 2 ? radius : 1;
    internal static TimeSpan Duration(TimeSpan duration, double speed) => TimeSpan.FromTicks((long)(duration.Ticks / Speed(speed)));
    internal static double ControlHeight(string scale) => Scale(scale) switch { "compact" => 28, "large" => 40, _ => 32 };
    internal static double NavigationHeight(string scale) => Scale(scale) switch { "compact" => 56, "large" => 76, _ => 60 };
    internal static double NavigationGap(string scale) => Scale(scale) switch { "compact" => 2, "large" => 8, _ => 4 };
    internal static double Gap(double value, string scale) => Scale(scale) switch
    {
        "compact" => Math.Max(0, Math.Min(4, value * .2)),
        "large" => value,
        _ => UiLayout.CompactGap(value)
    };
    internal static double Inset(double value, string scale) => Scale(scale) switch
    {
        "compact" => Math.Max(0, Math.Min(8, value * .25)),
        "large" => value,
        _ => UiLayout.CompactInset(value)
    };
    internal static string Remainder(string palette, string? color) => palette == "green" || color == "red" ? "red" : "green";
    internal static string FileNameFormat(string? format) =>
        format is "project-address-date" or "address-date" or "project-address" ? format : "project-date";

    internal static void CopyLocal(AppPreferences source, AppPreferences target)
    {
        target.InterfaceScale = Scale(source.InterfaceScale);
        target.MoreTransparency = source.MoreTransparency;
        target.AnimationSpeed = Speed(source.AnimationSpeed);
        target.CursorRevealRadius = Radius(source.CursorRevealRadius);
        target.CutFileNameFormat = FileNameFormat(source.CutFileNameFormat);
        target.DetailingFileNameFormat = FileNameFormat(source.DetailingFileNameFormat);
        target.RemainderColor = Remainder(target.CutPalette, source.RemainderColor);
        target.ContrastCutLines = source.ContrastCutLines;
    }
}
