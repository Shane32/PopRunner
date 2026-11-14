using System.Buffers.Binary;
using System.IO.Compression;

/// <summary>Independent straight RGBA decoding of the indexed/RGBA PNGs used in asset verification.</summary>
internal static class PngPixels
{
    internal static byte[] ReadRgba(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        int width = 0, height = 0, depth = 0, color = 0;
        byte[]? palette = null, alpha = null;
        using var compressed = new MemoryStream();
        for (int offset = 8; offset < file.Length;)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(offset)));
            uint type = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(offset + 4));
            ReadOnlySpan<byte> chunk = file.AsSpan(offset + 8, length);
            switch (type)
            {
                case 0x49484452:
                    width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(chunk));
                    height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(chunk[4..]));
                    depth = chunk[8];
                    color = chunk[9];
                    if (chunk[12] != 0 || !(color == 3 && depth is 1 or 2 or 4 or 8 || color == 6 && depth == 8))
                        throw new InvalidDataException("Unexpected verification PNG format.");
                    break;
                case 0x504C5445: palette = chunk.ToArray(); break;
                case 0x74524E53: alpha = chunk.ToArray(); break;
                case 0x49444154: compressed.Write(chunk); break;
            }
            offset = checked(offset + length + 12);
        }
        int rowBytes = checked((width * (color == 6 ? 32 : depth) + 7) / 8);
        int filterStride = color == 6 ? 4 : 1;
        byte[] previous = new byte[rowBytes], row = new byte[rowBytes];
        byte[] pixels = new byte[checked(width * height * 4)];
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        for (int y = 0; y < height; y++)
        {
            int filter = zlib.ReadByte();
            zlib.ReadExactly(row);
            for (int index = 0; index < row.Length; index++)
            {
                int left = index >= filterStride ? row[index - filterStride] : 0;
                int above = previous[index];
                int upperLeft = index >= filterStride ? previous[index - filterStride] : 0;
                int predictor = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => above,
                    3 => (left + above) / 2,
                    4 => Paeth(left, above, upperLeft),
                    _ => throw new InvalidDataException("Unexpected PNG filter.")
                };
                row[index] = unchecked((byte)(row[index] + predictor));
            }
            for (int x = 0; x < width; x++)
            {
                int target = (y * width + x) * 4;
                if (color == 6)
                    row.AsSpan(x * 4, 4).CopyTo(pixels.AsSpan(target));
                else
                {
                    int index = (row[x * depth / 8] >> (8 - depth - x * depth % 8)) & ((1 << depth) - 1);
                    (palette ?? throw new InvalidDataException("Missing palette.")).AsSpan(index * 3, 3).CopyTo(pixels.AsSpan(target));
                    pixels[target + 3] = alpha is not null && index < alpha.Length ? alpha[index] : (byte)255;
                }
            }
            (previous, row) = (row, previous);
        }
        if (zlib.ReadByte() != -1)
            throw new InvalidDataException("Unexpected PNG trailing scanline data.");
        return pixels;
    }

    private static int Paeth(int left, int above, int upperLeft)
    {
        int prediction = left + above - upperLeft;
        int a = Math.Abs(prediction - left), b = Math.Abs(prediction - above), c = Math.Abs(prediction - upperLeft);
        return a <= b && a <= c ? left : b <= c ? above : upperLeft;
    }
}
