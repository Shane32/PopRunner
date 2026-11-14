using System.Buffers.Binary;
using System.IO.Compression;

namespace PopRunner.ExtractAssets;

internal static class PngEncoder
{
    internal static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int stride = checked(width * 4);
        if (rgba.Length != checked(stride * height))
            throw new ArgumentException("Pixels must contain four RGBA bytes per pixel.", nameof(rgba));

        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)height);
        header[8] = 8;
        header[9] = 6; // Eight-bit RGBA; remaining fields select standard compression and no interlacing.
        WriteChunk(output, 0x49484452, header); // IHDR

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            ReadOnlySpan<byte> filter = [0]; // The None filter preserves each top-down RGBA row.
            for (int y = 0; y < height; y++)
            {
                zlib.Write(filter);
                zlib.Write(rgba.Slice(y * stride, stride));
            }
        }

        WriteChunk(output, 0x49444154, compressed.GetBuffer().AsSpan(0, checked((int)compressed.Length))); // IDAT
        WriteChunk(output, 0x49454E44, []); // IEND
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, uint type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)data.Length);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], type);
        output.Write(header);
        output.Write(data);

        uint crc = uint.MaxValue;
        foreach (byte value in header[4..]) crc = UpdateCrc(crc, value);
        foreach (byte value in data) crc = UpdateCrc(crc, value);
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, ~crc);
        output.Write(checksum);
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
            crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xEDB88320;
        return crc;
    }
}
