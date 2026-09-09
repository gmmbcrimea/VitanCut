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
