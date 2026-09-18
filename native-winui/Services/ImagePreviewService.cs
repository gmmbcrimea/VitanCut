using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace VitanCut.WinUI.Services;

public static class ImagePreviewService
{
    public static async Task<bool> SetAsync(Image image, string source)
    {
        if (ProductImageData.Read(source) is not { } bytes) return false;
        try
        {
            // Downsample large source images with the high-quality decoder interpolation
            // before WinUI renders them in a smaller thumbnail or preview.
            if (await ImageResizeService.ResizeAsync(bytes, 300, 300) is { } resized && ProductImageData.Read(resized) is { } resizedBytes)
                bytes = resizedBytes;
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
            }
            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            image.Source = bitmap;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException or IOException)
        {
            return false;
        }
    }
}
