using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.CompilerServices;
using Windows.Storage.Streams;

namespace VitanCut.WinUI.Services;

public static class ImagePreviewService
{
    private static readonly ConditionalWeakTable<Image, UiRequestGate> Requests = new();

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
        BitmapImage? bitmap = null;
        try
        {
            if (ProductImageData.Read(source) is { } bytes)
            {
                // Downsample before WinUI renders the thumbnail at its final size.
                if (await ImageResizeService.ResizeAsync(bytes, 300, 300) is { } resized && ProductImageData.Read(resized) is { } resizedBytes)
                    bytes = resizedBytes;
                if (!Current()) return false;
                using var stream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(bytes);
                    await writer.StoreAsync();
                }
                stream.Seek(0);
                bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException or IOException)
        {
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
}
