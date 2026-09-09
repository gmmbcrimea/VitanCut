namespace VitanCut.WinUI.Services;

public static class ProductImageData
{
    private const int MaxBytes = 20 * 1024 * 1024;

    public static byte[]? Read(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        try
        {
            if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var comma = source.IndexOf(',');
                if (comma < 0 || !source[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase) || source.Length > MaxBytes * 4L / 3 + 100) return null;
                return Convert.FromBase64String(source[(comma + 1)..]);
            }
            var file = new FileInfo(source);
            return file.Exists && file.Length <= MaxBytes ? File.ReadAllBytes(source) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
