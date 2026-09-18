using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using System.Runtime.InteropServices;

namespace VitanCut.WinUI.Services;

public static class ImageResizeService
{
    public static async Task<string?> FromFileAsync(string path, uint maxWidth, uint maxHeight, bool cropToBounds = false)
    {
        try
        {
            await using var input = File.OpenRead(path);
            using var source = new MemoryStream();
            await input.CopyToAsync(source);
            return await ResizeAsync(source.ToArray(), maxWidth, maxHeight, cropToBounds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public static async Task<string?> ResizeAsync(byte[] bytes, uint maxWidth, uint maxHeight, bool cropToBounds = false)
    {
        if (bytes.Length == 0) return null;
        try
        {
            using var input = new InMemoryRandomAccessStream();
            await WriteAsync(input, bytes);
            input.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(input);
            var scale = cropToBounds
                ? Math.Max((double)maxWidth / decoder.PixelWidth, (double)maxHeight / decoder.PixelHeight)
                : Math.Min(1, Math.Min((double)maxWidth / decoder.PixelWidth, (double)maxHeight / decoder.PixelHeight));
            var scaledWidth = Math.Max(1u, (uint)Math.Round(decoder.PixelWidth * scale));
            var scaledHeight = Math.Max(1u, (uint)Math.Round(decoder.PixelHeight * scale));
            var width = cropToBounds ? maxWidth : scaledWidth;
            var height = cropToBounds ? maxHeight : scaledHeight;
            var transform = new BitmapTransform
            {
                ScaledWidth = scaledWidth,
                ScaledHeight = scaledHeight,
                InterpolationMode = BitmapInterpolationMode.Fant
            };
            if (cropToBounds)
            {
                transform.Bounds = new BitmapBounds
                {
                    X = (scaledWidth - width) / 2,
                    Y = (scaledHeight - height) / 2,
                    Width = width,
                    Height = height
                };
            }
            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage);

            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, width, height, 96, 96, pixels.DetachPixelData());
            await encoder.FlushAsync();
            output.Seek(0);
            var reader = new DataReader(output.GetInputStreamAt(0));
            await reader.LoadAsync((uint)output.Size);
            var result = new byte[(int)output.Size];
            reader.ReadBytes(result);
            return "data:image/png;base64," + Convert.ToBase64String(result);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or COMException)
        {
            return null;
        }
    }

    private static async Task WriteAsync(IRandomAccessStream stream, byte[] bytes)
    {
        var writer = new DataWriter(stream.GetOutputStreamAt(0));
        writer.WriteBytes(bytes);
        await writer.StoreAsync();
        await writer.FlushAsync();
        writer.DetachStream();
    }
}
