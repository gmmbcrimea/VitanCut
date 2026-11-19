namespace VitanCut.WinUI.Services;

public static class ImagePreviewSize
{
    public const uint ImportLimit = 1024;

    public static uint Pixels(double requested, double actual, double scale)
    {
        var size = double.IsFinite(requested) && requested > 0 ? requested
            : double.IsFinite(actual) && actual > 0 ? actual : 300;
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        return (uint)Math.Clamp(Math.Ceiling(size * scale), 1, ImportLimit);
    }
}
