using VitanCut.WinUI.Services;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

internal static class ThumbnailChecks
{
    public static async Task RunAsync()
    {
        var checks = 0;
        foreach (var size in new[] { 300u, 1024u })
        {
            var pixels = new byte[size * size * 4];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var i = (y * size + x) * 4;
                    var value = (byte)(x > y ? 255 : 0);
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
                    pixels[i + 3] = 255;
                }
            using var input = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, input);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, size, size, 96, 96, pixels);
            await encoder.FlushAsync();
            using var reader = new DataReader(input.GetInputStreamAt(0));
            await reader.LoadAsync((uint)input.Size);
            var original = new byte[(int)input.Size];
            reader.ReadBytes(original);
            foreach (var target in new[] { 40u, 56u, 84u, 112u })
            {
                var resized = await ImageResizeService.ResizeAsync(original, target, target)
                    ?? throw new InvalidOperationException("Resize failed");
                var bytes = Convert.FromBase64String(resized[(resized.IndexOf(',') + 1)..]);
                using var output = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(output.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(bytes);
                    await writer.StoreAsync();
                }
                output.Seek(0);
                var decoder = await BitmapDecoder.CreateAsync(output);
                Assert(decoder.PixelWidth == target && decoder.PixelHeight == target, $"{size}px source renders at {target}px");
                var result = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
                var mixed = 0;
                for (var y = 1; y < target - 1; y++)
                    if (result[(y * target + y) * 4] is > 5 and < 250) mixed++;
                Assert(mixed > target / 2, "Diagonal edges are antialiased for both legacy and new images");
                Assert(result[(target - 1) * 4] > 250 && result[((target - 1) * target) * 4] < 5, "Solid areas remain sharp");
            }
        }
        Assert(await ImageResizeService.ResizeAsync([], 56, 56) is null, "Invalid images are rejected");
        Console.WriteLine($"{checks} Windows image checks passed");

        void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
            Console.WriteLine("PASS: " + message);
        }
    }
}
