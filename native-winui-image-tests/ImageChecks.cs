using System.Diagnostics;
using System.Text.Json;
using VitanCut.WinUI.Services;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

internal static class ImageChecks
{
    [STAThread]
    private static async Task<int> Main()
    {
        var imagePath = Path.GetFullPath("native-winui/Assets/logo.png");
        var source = await File.ReadAllBytesAsync(imagePath);
        var sourceDimensions = await Dimensions(source);

        var watch = Stopwatch.StartNew();
        var productData = await ImageResizeService.ResizeAsync(source, 300, 300);
        watch.Stop();
        var productBytes = ProductImageData.Read(productData ?? "")
            ?? throw new InvalidOperationException("Product resize returned invalid image data.");
        var productDimensions = await Dimensions(productBytes);
        Require(productDimensions.Width <= 300 && productDimensions.Height <= 300, "Product image stays within 300px bounds");
        Require(AspectClose(sourceDimensions, productDimensions), "Product image preserves source aspect ratio");

        var secondPass = ProductImageData.Read(await ImageResizeService.ResizeAsync(productBytes, 300, 300) ?? "")
            ?? throw new InvalidOperationException("Second resize returned invalid image data.");
        var secondDimensions = await Dimensions(secondPass);
        Require(secondDimensions == productDimensions, "Repeated product resize does not change output dimensions");

        var checkerboard = await CheckerboardPng(512, 512);
        var smoothedData = await ImageResizeService.ResizeAsync(checkerboard, 100, 100);
        var smoothed = ProductImageData.Read(smoothedData ?? "")
            ?? throw new InvalidOperationException("Checkerboard resize returned invalid data.");
        var centerPixel = await PixelAt(smoothed, 50, 50);
        Require(centerPixel.R is > 80 and < 175 && centerPixel.G == centerPixel.R && centerPixel.B == centerPixel.R,
            "Fant interpolation smooths high-frequency image detail");

        var textureData = await ImageResizeService.ResizeAsync(source, 100, 100, cropToBounds: true);
        var textureBytes = ProductImageData.Read(textureData ?? "")
            ?? throw new InvalidOperationException("Texture crop returned invalid image data.");
        var textureDimensions = await Dimensions(textureBytes);
        Require(textureDimensions == (100u, 100u), "Texture crop produces exactly 100x100 pixels");

        foreach (var dimensions in new[] { (Width: 4096u, Height: 1u), (Width: 1u, Height: 4096u), (Width: 1200u, Height: 80u), (Width: 80u, Height: 1200u), (Width: 1u, Height: 1u) })
        {
            var edgeImage = await SolidPng(dimensions.Width, dimensions.Height);
            var resizedData = await ImageResizeService.ResizeAsync(edgeImage, 300, 300);
            var resized = ProductImageData.Read(resizedData ?? "")
                ?? throw new InvalidOperationException($"Resize returned invalid data for {dimensions.Width}x{dimensions.Height}.");
            var resizedDimensions = await Dimensions(resized);
            Require(resizedDimensions.Width <= 300 && resizedDimensions.Height <= 300, $"Product resize bounds hold for {dimensions.Width}x{dimensions.Height}");
            Require(AspectClose(dimensions, resizedDimensions), $"Product aspect ratio holds for {dimensions.Width}x{dimensions.Height}");

            var croppedData = await ImageResizeService.ResizeAsync(edgeImage, 100, 100, cropToBounds: true);
            var cropped = ProductImageData.Read(croppedData ?? "")
                ?? throw new InvalidOperationException($"Crop returned invalid data for {dimensions.Width}x{dimensions.Height}.");
            Require(await Dimensions(cropped) == (100u, 100u), $"Texture crop bounds hold for {dimensions.Width}x{dimensions.Height}");
        }

        Require(await ImageResizeService.ResizeAsync([], 100, 100) is null, "Empty image input is rejected");
        Require(await ImageResizeService.ResizeAsync([0, 1, 2, 3], 100, 100) is null, "Malformed image input is rejected");
        Require(ProductImageData.Read("data:image/png;base64,not-base64") is null, "Malformed image data URI is rejected");

        for (ushort orientation = 2; orientation <= 8; orientation++)
        {
            var orientedJpeg = await CornerJpegWithOrientation(80, 120, orientation);
            var expectedDimensions = orientation >= 5 ? (120u, 80u) : (80u, 120u);
            var orientedProductData = await ImageResizeService.ResizeAsync(orientedJpeg, 300, 300);
            var orientedProduct = ProductImageData.Read(orientedProductData ?? "")
                ?? throw new InvalidOperationException($"EXIF {orientation} product resize returned invalid data.");
            Require(await Dimensions(orientedProduct) == expectedDimensions, $"Product resize normalizes EXIF orientation {orientation}");
            if (orientation == 6)
            {
                Require(IsDominant(await PixelAt(orientedProduct, 10, 10), "blue")
                    && IsDominant(await PixelAt(orientedProduct, 110, 10), "red")
                    && IsDominant(await PixelAt(orientedProduct, 10, 70), "yellow")
                    && IsDominant(await PixelAt(orientedProduct, 110, 70), "green"),
                    "EXIF 90-degree rotation transforms actual image pixels");
            }
            var orientedTextureData = await ImageResizeService.ResizeAsync(orientedJpeg, 100, 100, cropToBounds: true);
            var orientedTexture = ProductImageData.Read(orientedTextureData ?? "")
                ?? throw new InvalidOperationException($"EXIF {orientation} texture crop returned invalid data.");
            Require(await Dimensions(orientedTexture) == (100u, 100u), $"Texture crop supports EXIF orientation {orientation}");
        }

        var cloudJson = JsonSerializer.Serialize(new { image = productData });
        Console.WriteLine($"Source: {sourceDimensions.Width}x{sourceDimensions.Height}, {source.Length:N0} bytes");
        Console.WriteLine($"Product: {productDimensions.Width}x{productDimensions.Height}, {productBytes.Length:N0} bytes; resize {watch.Elapsed.TotalMilliseconds:F1} ms");
        Console.WriteLine($"Texture: {textureDimensions.Width}x{textureDimensions.Height}, {textureBytes.Length:N0} bytes");
        Console.WriteLine($"Synthetic image field in cloud JSON: {System.Text.Encoding.UTF8.GetByteCount(cloudJson):N0} bytes");
        return 0;
    }

    private static async Task<(uint Width, uint Height)> Dimensions(byte[] bytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(stream.GetOutputStreamAt(0));
        writer.WriteBytes(bytes);
        await writer.StoreAsync();
        writer.DetachStream();
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        return (decoder.OrientedPixelWidth, decoder.OrientedPixelHeight);
    }

    private static async Task<byte[]> SolidPng(uint width, uint height)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var pixels = new byte[checked((int)(width * height * 4))];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 0x40;
            pixels[index + 1] = 0x80;
            pixels[index + 2] = 0xC0;
            pixels[index + 3] = 0xFF;
        }
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, width, height, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        var result = new byte[(int)stream.Size];
        reader.ReadBytes(result);
        return result;
    }

    private static async Task<byte[]> CheckerboardPng(uint width, uint height)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var pixels = new byte[checked((int)(width * height * 4))];
        for (uint y = 0; y < height; y++)
        for (uint x = 0; x < width; x++)
        {
            var offset = checked((int)((y * width + x) * 4));
            var shade = (byte)(((x + y) & 1) == 0 ? 0 : 255);
            pixels[offset] = shade;
            pixels[offset + 1] = shade;
            pixels[offset + 2] = shade;
            pixels[offset + 3] = 255;
        }
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, width, height, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        var result = new byte[(int)stream.Size];
        reader.ReadBytes(result);
        return result;
    }

    private static async Task<(byte R, byte G, byte B)> PixelAt(byte[] bytes, uint x, uint y)
    {
        using var stream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(stream.GetOutputStreamAt(0));
        writer.WriteBytes(bytes);
        await writer.StoreAsync();
        writer.DetachStream();
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);
        var data = pixels.DetachPixelData();
        var offset = checked((int)((y * decoder.OrientedPixelWidth + x) * 4));
        return (data[offset + 2], data[offset + 1], data[offset]);
    }

    private static async Task<byte[]> CornerJpegWithOrientation(uint width, uint height, ushort orientation)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
        var pixels = new byte[checked((int)(width * height * 4))];
        for (uint y = 0; y < height; y++)
        for (uint x = 0; x < width; x++)
        {
            var offset = checked((int)((y * width + x) * 4));
            var (b, g, r) = (x < width / 2, y < height / 2) switch
            {
                (true, true) => ((byte)0, (byte)0, (byte)255),
                (false, true) => ((byte)0, (byte)255, (byte)0),
                (true, false) => ((byte)255, (byte)0, (byte)0),
                _ => ((byte)0, (byte)255, (byte)255)
            };
            pixels[offset] = b;
            pixels[offset + 1] = g;
            pixels[offset + 2] = r;
            pixels[offset + 3] = 255;
        }
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, width, height, 96, 96, pixels);
        await encoder.BitmapProperties.SetPropertiesAsync(new[]
        {
            new KeyValuePair<string, BitmapTypedValue>(
                "System.Photo.Orientation",
                new BitmapTypedValue(orientation, Windows.Foundation.PropertyType.UInt16))
        });
        await encoder.FlushAsync();
        stream.Seek(0);
        var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        var result = new byte[(int)stream.Size];
        reader.ReadBytes(result);
        return result;
    }

    private static bool IsDominant((byte R, byte G, byte B) pixel, string color) => color switch
    {
        "red" => pixel.R > pixel.G + 60 && pixel.R > pixel.B + 60,
        "green" => pixel.G > pixel.R + 60 && pixel.G > pixel.B + 60,
        "blue" => pixel.B > pixel.R + 60 && pixel.B > pixel.G + 60,
        "yellow" => pixel.R > 150 && pixel.G > 150 && pixel.B < 100,
        _ => false
    };

    private static bool AspectClose((uint Width, uint Height) source, (uint Width, uint Height) resized) =>
        Math.Abs((long)source.Width * resized.Height - (long)source.Height * resized.Width) <= Math.Max(source.Width, source.Height);

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine($"PASS: {description}");
    }
}
