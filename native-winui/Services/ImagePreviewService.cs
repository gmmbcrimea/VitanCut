using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Windows.Storage.Streams;

namespace VitanCut.WinUI.Services;

public static class ImagePreviewService
{
    private static readonly ConditionalWeakTable<Image, UiRequestGate> Requests = new();
    private static readonly Dictionary<(string Source, uint Width, uint Height), Task<BitmapImage?>> Cache = [];
    private static readonly SemaphoreSlim Resizers = new(2);

    public static Task<bool> SetAsync(Image image, string source) => LoadAsync(image, source, null, null);

    internal static Task<bool> SetVisibleAsync(Image image, string source, Action<bool>? onCurrentCompleted = null, Func<bool>? isCurrent = null) =>
        LoadAsync(image, source, loaded =>
        {
            image.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;
            onCurrentCompleted?.Invoke(loaded);
        }, isCurrent);

    private static async Task<bool> LoadAsync(Image image, string source, Action<bool>? onCurrentCompleted, Func<bool>? isCurrent)
    {
        using var measurement = App.UiPerformance.Measure(UiWork.ImagePreview, asynchronous: true);
        var gate = Requests.GetValue(image, _ => new UiRequestGate());
        var request = gate.Begin();
        bool Current() => gate.IsCurrent(request) && (isCurrent?.Invoke() ?? true);
        if (!image.IsLoaded)
        {
            RoutedEventHandler? onLoaded = null;
            onLoaded = async (_, _) =>
            {
                image.Loaded -= onLoaded;
                if (Current()) await LoadAsync(image, source, onCurrentCompleted, isCurrent);
            };
            image.Loaded += onLoaded;
        }
        BitmapImage? bitmap = null;
        try
        {
            var scale = image.XamlRoot?.RasterizationScale ?? 1;
            var width = ImagePreviewSize.Pixels(image.Width, image.ActualWidth, scale);
            var height = ImagePreviewSize.Pixels(image.Height, image.ActualHeight, scale);
            if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var key = (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))), width, height);
                if (!Cache.TryGetValue(key, out var pending))
                {
                    // Bound retained decoded images; identical rows share one in-flight resize.
                    if (Cache.Count >= 64) Cache.Remove(Cache.Keys.First());
                    Cache[key] = pending = CreateBitmapAsync(source, width, height);
                }
                bitmap = await pending;
                if (bitmap is null) Cache.Remove(key);
            }
            else bitmap = await CreateBitmapAsync(source, width, height);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException or IOException)
        {
            Cache.Clear();
            bitmap = null;
        }
        // Image and its visibility/status must belong to the same latest request.
        if (!Current()) return false;
        try
        {
            image.Source = bitmap;
            var loaded = bitmap is not null;
            onCurrentCompleted?.Invoke(loaded);
            return loaded;
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException or IOException)
        {
            return false;
        }
    }

    private static async Task<BitmapImage?> CreateBitmapAsync(string source, uint width, uint height)
    {
        await Resizers.WaitAsync();
        byte[]? bytes;
        try
        {
            bytes = await Task.Run(async () =>
            {
                if (ProductImageData.Read(source) is not { } original) return null;
                var resized = await ImageResizeService.ResizeAsync(original, width, height);
                return resized is null ? null : ProductImageData.Read(resized);
            });
        }
        finally { Resizers.Release(); }
        if (bytes is null) return null;
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
        }
        stream.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}
