#if POP_RUNNER_CHECKS
using System.Buffers.Binary;
using System.IO.Compression;
using PopRunner.Core;

namespace PopRunner;

/// <summary>Exercises the same managed decoder and Win32 DIB renderer used by the tray application.</summary>
internal static class NativeImageChecks
{
    internal static void Run(Action<bool, string> check, Action<string>? phase = null)
    {
        phase?.Invoke("Decode embedded PNGs");
        bool hasTransparent = false;
        bool hasOpaque = false;
        check(AnimationResources.ImagePaths.Count > 0, "The core contains embedded PNG assets.");
        foreach (string path in AnimationResources.ImagePaths)
        {
            using Stream stream = AnimationResources.OpenImage(path);
            PngImage image = PngImage.Load(stream);
            check(image.Width > 0 && image.Height > 0 && image.Pixels.Length == image.Width * image.Height * 4,
                $"Decoded embedded PNG dimensions: {path}");
            check(stream.CanRead, $"PNG decoder preserves caller stream ownership: {path}");
            bool premultiplied = true;
            for (int pixel = 0; pixel < image.Pixels.Length; pixel += 4)
            {
                byte alpha = image.Pixels[pixel + 3];
                premultiplied &= image.Pixels[pixel] <= alpha && image.Pixels[pixel + 1] <= alpha && image.Pixels[pixel + 2] <= alpha;
                hasTransparent |= alpha == 0;
                hasOpaque |= alpha == 255;
            }
            check(premultiplied, $"Decoded PNG has premultiplied BGRA pixels: {path}");
        }
        check(hasTransparent && hasOpaque, "Original PNGs retain both transparent and visible pixels.");

        phase?.Invoke("Native DIB checks");
        var twoPixels = new PngImage(2, 1, [0, 0, 255, 255, 0, 128, 0, 128]);
        using (var surface = new LayeredSurface(8, 6))
        {
            phase?.Invoke("Native DIB created");
            check(surface.Width == 8 && surface.Height == 6 && surface.Pixels.Length == 8 * 6 * 4,
                "Native DIB dimensions and top-down byte count are correct.");
            check(surface.Pixels.IndexOfAnyExcept((byte)0) == -1, "A new native DIB is fully transparent.");
            surface.Draw(twoPixels, -1, 1, 2, 2.4, false);
            check(surface.Pixels[(1 * 8) * 4 + 2] == 255 && surface.Pixels[(1 * 8) * 4 + 3] == 255,
                "Nearest-neighbor drawing clips the left image edge without shifting source pixels.");
            check(surface.Pixels[(1 * 8 + 1) * 4 + 1] == 128 && surface.Pixels[(3 * 8 + 2) * 4 + 3] == 128,
                "Fractional vertical scale preserves alpha and rounds its destination height up.");
            check(surface.Pixels[(0 * 8 + 1) * 4 + 3] == 0 && surface.Pixels[(4 * 8 + 1) * 4 + 3] == 0 &&
                surface.Pixels[(1 * 8 + 3) * 4 + 3] == 0,
                "Pixels outside the scaled sprite remain transparent.");
            surface.Draw(twoPixels, 1, 1, 1, 1, true);
            check(surface.Pixels[(1 * 8 + 1) * 4 + 1] == 192 && surface.Pixels[(1 * 8 + 1) * 4 + 3] == 192,
                "Overlapping translucent pixels use premultiplied source-over compositing.");
            check(surface.Pixels[(1 * 8 + 2) * 4 + 2] == 255 && surface.Pixels[(1 * 8 + 2) * 4 + 3] == 255,
                "Mirroring reverses source pixels and opaque pixels replace the destination.");
            surface.Clear();
            check(surface.Pixels.IndexOfAnyExcept((byte)0) == -1, "Clear removes all color and alpha from the native DIB.");
            surface.Draw(twoPixels, 7, 5, 2, 2, false);
            check(surface.Pixels[(5 * 8 + 7) * 4 + 2] == 255 && surface.Pixels[(5 * 8 + 6) * 4 + 3] == 0,
                "Drawing clips safely at the bottom and right window edges.");
            surface.Clear();
            surface.Draw(twoPixels, int.MinValue, int.MinValue, 2, 2, false);
            surface.Draw(twoPixels, int.MaxValue, int.MaxValue, 2, 2, false);
            check(surface.Pixels.IndexOfAnyExcept((byte)0) == -1, "Entirely offscreen sprites do not overflow or modify the DIB.");
        }

        phase?.Invoke("PNG codec checks");
        byte[] diagnosticPixels = [0, 0, 255, 255, 32, 64, 96, 128, 0, 0, 0, 0, 255, 255, 255, 255];
        using var encoded = new MemoryStream();
        WritePng(encoded, 2, 2, diagnosticPixels);
        byte[] png = encoded.ToArray();
        encoded.Position = 0;
        PngImage decoded = PngImage.Load(encoded);
        check(decoded.Width == 2 && decoded.Height == 2 && decoded.Pixels.AsSpan().SequenceEqual(diagnosticPixels),
            "Diagnostic PNG encoding round-trips color, translucency, transparency, and top-down row order.");
        bool rejectedTruncation = false;
        try { PngImage.Load(new MemoryStream(png[..^1], writable: false)); }
        catch (InvalidDataException) { rejectedTruncation = true; }
        check(rejectedTruncation, "PNG decoder rejects truncated input.");
        png[29] ^= 1; // IHDR checksum; its metadata and image data remain intact.
        bool rejectedCrc = false;
        try { PngImage.Load(new MemoryStream(png, writable: false)); }
        catch (InvalidDataException) { rejectedCrc = true; }
        check(rejectedCrc, "PNG decoder rejects damaged chunk checksums.");
    }

    /// <summary>Saves diagnostic pixels without System.Drawing, desktop frameworks, or COM.</summary>
    internal static void SavePng(string path, int width, int height, ReadOnlySpan<byte> premultipliedBgra)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        WritePng(file, width, height, premultipliedBgra);
    }

    private static void WritePng(Stream stream, int width, int height, ReadOnlySpan<byte> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != checked(width * height * 4))
            throw new ArgumentException("Diagnostic pixels must contain four BGRA bytes per pixel.", nameof(pixels));
        stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)height);
        header[8] = 8;
        header[9] = 6; // RGBA, eight bits per channel, no interlacing.
        WriteChunk(stream, 0x49484452, header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[checked(width * 4 + 1)]; // Leading zero selects the PNG "None" filter.
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int source = (y * width + x) * 4;
                    int destination = x * 4 + 1;
                    int alpha = pixels[source + 3];
                    row[destination] = Unpremultiply(pixels[source + 2], alpha);
                    row[destination + 1] = Unpremultiply(pixels[source + 1], alpha);
                    row[destination + 2] = Unpremultiply(pixels[source], alpha);
                    row[destination + 3] = (byte)alpha;
                }
                zlib.Write(row);
            }
        }
        WriteChunk(stream, 0x49444154, compressed.GetBuffer().AsSpan(0, checked((int)compressed.Length)));
        WriteChunk(stream, 0x49454E44, []);
    }

    private static byte Unpremultiply(int component, int alpha) =>
        alpha == 0 ? (byte)0 : (byte)Math.Min(255, (component * 255 + alpha / 2) / alpha);

    private static void WriteChunk(Stream stream, uint type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)data.Length);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], type);
        stream.Write(header);
        stream.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte value in header[4..]) crc = UpdateCrc(crc, value);
        foreach (byte value in data) crc = UpdateCrc(crc, value);
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, ~crc);
        stream.Write(checksum);
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
            crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        return crc;
    }
}
#endif
