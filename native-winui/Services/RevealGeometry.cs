namespace VitanCut.WinUI.Services;

internal readonly record struct RevealBounds(double X, double Y, double Width, double Height);

internal static class RevealGeometry
{
    public static bool HasVisibleEdge(byte borderAlpha, double borderWidth, uint fill, uint backdrop)
    {
        if (borderAlpha > 8 && borderWidth > 0) return true;
        var alpha = (fill >> 24) / 255d;
        if (alpha <= 8 / 255d) return false;
        for (var shift = 0; shift <= 16; shift += 8)
        {
            var foreground = (fill >> shift) & 255;
            var background = (backdrop >> shift) & 255;
            if (Math.Abs(foreground - (double)background) * alpha > 8) return true;
        }
        return false;
    }

    public static RevealBounds Clip(RevealBounds bounds, RevealBounds viewport)
    {
        var x = Math.Max(bounds.X, viewport.X);
        var y = Math.Max(bounds.Y, viewport.Y);
        return new RevealBounds(x, y,
            Math.Max(0, Math.Min(bounds.X + bounds.Width, viewport.X + viewport.Width) - x),
            Math.Max(0, Math.Min(bounds.Y + bounds.Height, viewport.Y + viewport.Height) - y));
    }
}
