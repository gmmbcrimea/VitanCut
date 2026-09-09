namespace VitanCut.WinUI.Models;

public sealed record AccentOption(string Id, string Name, byte R, byte G, byte B);

public static class AccentPalette
{
    public static IReadOnlyList<AccentOption> Options { get; } = Array.AsReadOnly(new[]
    {
        new AccentOption("system", "Системный", 128, 128, 128),
        new AccentOption("blue", "Синий", 0, 108, 224),
        new AccentOption("green", "Зелёный", 16, 124, 65),
        new AccentOption("graphite", "Графит", 82, 87, 96),
        new AccentOption("teal", "Бирюзовый", 8, 127, 140),
        new AccentOption("cyan", "Голубой", 8, 126, 164),
        new AccentOption("violet", "Фиолетовый", 121, 84, 205),
        new AccentOption("pink", "Розовый", 182, 62, 135),
        new AccentOption("coral", "Коралловый", 201, 68, 85),
        new AccentOption("amber", "Янтарный", 245, 197, 66)
    });

    public static AccentOption Find(string? id) => Options.FirstOrDefault(x => x.Id == id) ?? Options[0];

    public static bool UseLightForeground(byte r, byte g, byte b) =>
        ForegroundContrast(r, g, b, true) >= ForegroundContrast(r, g, b, false);

    // Check both ends of the accent surface, which darkens by 22%.
    public static double ForegroundContrast(byte r, byte g, byte b, bool light)
    {
        static double Linear(double channel)
        {
            channel /= 255;
            return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
        }
        var scale = light ? 1 : .78;
        var luminance = .2126 * Linear((byte)(r * scale)) + .7152 * Linear((byte)(g * scale)) + .0722 * Linear((byte)(b * scale));
        return light ? 1.05 / (luminance + .05) : (luminance + .05) / .05;
    }
}
