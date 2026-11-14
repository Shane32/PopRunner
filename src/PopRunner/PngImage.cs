using System.Buffers.Binary;
using System.IO.Compression;

namespace PopRunner;

/// <summary>A top-down PNG bitmap in premultiplied BGRA order, suitable for a Win32 DIB.</summary>
internal sealed class PngImage
{
    private const int MaximumImageBytes = 256 * 1024 * 1024;
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = CreateCrcTable();

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    internal PngImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != checked(width * height * 4))
            throw new ArgumentException("Pixel data must contain exactly four BGRA bytes per pixel.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Decodes a noninterlaced PNG without taking ownership of the input stream.</summary>
    public static PngImage Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            return Decode(stream);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("The PNG is truncated.", exception);
        }
    }

    private static PngImage Decode(Stream stream)
    {
        Span<byte> signature = stackalloc byte[8];
        stream.ReadExactly(signature);
        if (!signature.SequenceEqual(Signature))
            throw new InvalidDataException("The image does not have a PNG signature.");

        int width = 0, height = 0, depth = 0, color = 0, channels = 0;
        byte[]? palette = null;
        byte[]? transparency = null;
        bool hasHeader = false, hasData = false, dataEnded = false;
        using var compressed = new MemoryStream();
        Span<byte> chunkHeader = stackalloc byte[8];
        Span<byte> checksumBytes = stackalloc byte[4];

        while (true)
        {
            stream.ReadExactly(chunkHeader);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader);
            uint type = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader[4..]);
            if (length > MaximumImageBytes)
                throw new InvalidDataException("The PNG chunk exceeds the supported size.");
            var chunk = new byte[(int)length];
            stream.ReadExactly(chunk);
            stream.ReadExactly(checksumBytes);
            uint expected = BinaryPrimitives.ReadUInt32BigEndian(checksumBytes);
            if (CalculateCrc(chunkHeader[4..], chunk) != expected)
                throw new InvalidDataException("The PNG contains a chunk with an invalid CRC.");
            if (!hasHeader && type != 0x49484452) // IHDR
                throw new InvalidDataException("The PNG must begin with IHDR.");
            if (hasData && type != 0x49444154)
                dataEnded = true;

            switch (type)
            {
                case 0x49484452: // IHDR
                    if (hasHeader || chunk.Length != 13)
                        throw new InvalidDataException("The PNG has an invalid IHDR chunk.");
                    uint unsignedWidth = BinaryPrimitives.ReadUInt32BigEndian(chunk);
                    uint unsignedHeight = BinaryPrimitives.ReadUInt32BigEndian(chunk.AsSpan(4));
                    if (unsignedWidth == 0 || unsignedHeight == 0 ||
                        unsignedWidth > 32768 || unsignedHeight > 32768 ||
                        (ulong)unsignedWidth * unsignedHeight * 4 > MaximumImageBytes)
                        throw new InvalidDataException("The PNG dimensions exceed the supported size.");
                    width = (int)unsignedWidth;
                    height = (int)unsignedHeight;
                    depth = chunk[8];
                    color = chunk[9];
                    channels = color switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
                    bool validDepth = color switch
                    {
                        0 => depth is 1 or 2 or 4 or 8 or 16,
                        3 => depth is 1 or 2 or 4 or 8,
                        2 or 4 or 6 => depth is 8 or 16,
                        _ => false
                    };
                    if (!validDepth || chunk[10] != 0 || chunk[11] != 0)
                        throw new InvalidDataException("The PNG color depth, compression, or filter method is unsupported.");
                    if (chunk[12] != 0)
                        throw new InvalidDataException("Interlaced PNG images are unsupported; use a noninterlaced PNG.");
                    hasHeader = true;
                    break;

                case 0x504C5445: // PLTE
                    if (hasData || palette is not null || chunk.Length is 0 or > 768 || chunk.Length % 3 != 0)
                        throw new InvalidDataException("The PNG has an invalid palette.");
                    if (color is 0 or 4 || (color == 3 && chunk.Length / 3 > 1 << depth))
                        throw new InvalidDataException("The PNG palette does not match its color type.");
                    palette = chunk;
                    break;

                case 0x74524E53: // tRNS
                    if (hasData || transparency is not null || !(color switch
                    {
                        0 => chunk.Length == 2,
                        2 => chunk.Length == 6,
                        3 => palette is not null && chunk.Length <= palette.Length / 3,
                        _ => false
                    }))
                        throw new InvalidDataException("The PNG has an invalid transparency chunk.");
                    transparency = chunk;
                    break;

                case 0x49444154: // IDAT
                    if (dataEnded || (color == 3 && palette is null))
                        throw new InvalidDataException("The PNG has incorrectly ordered image data or no palette.");
                    if (compressed.Length + chunk.Length > MaximumImageBytes)
                        throw new InvalidDataException("The PNG compressed data exceeds the supported size.");
                    compressed.Write(chunk);
                    hasData = true;
                    break;

                case 0x49454E44: // IEND
                    if (chunk.Length != 0 || !hasData)
                        throw new InvalidDataException("The PNG has an invalid IEND chunk or no image data.");
                    compressed.Position = 0;
                    return DecodePixels(compressed, width, height, depth, color, channels, palette, transparency);

                default:
                    if ((chunkHeader[4] & 0x20) == 0)
                        throw new InvalidDataException("The PNG contains an unsupported critical chunk.");
                    break;
            }
        }
    }

    private static PngImage DecodePixels(Stream compressed, int width, int height, int depth, int color,
        int channels, byte[]? palette, byte[]? transparency)
    {
        int rowLength = checked((width * channels * depth + 7) / 8);
        int filterStride = Math.Max(1, (channels * depth + 7) / 8);
        byte[] previous = new byte[rowLength];
        byte[] row = new byte[rowLength];
        byte[] pixels = new byte[checked(width * height * 4)];
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true);
        for (int y = 0; y < height; y++)
        {
            int filter = zlib.ReadByte();
            if (filter == -1)
                throw new InvalidDataException("The PNG image data is truncated.");
            zlib.ReadExactly(row);
            for (int i = 0; i < row.Length; i++)
            {
                int left = i >= filterStride ? row[i - filterStride] : 0;
                int above = previous[i];
                int upperLeft = i >= filterStride ? previous[i - filterStride] : 0;
                int predictor = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => above,
                    3 => (left + above) / 2,
                    4 => Paeth(left, above, upperLeft),
                    _ => throw new InvalidDataException("The PNG uses an invalid scanline filter.")
                };
                row[i] = unchecked((byte)(row[i] + predictor));
            }

            for (int x = 0; x < width; x++)
            {
                int red, green, blue, alpha = 255;
                if (color == 3)
                {
                    int index = ReadSample(row, x, depth);
                    if (palette is null || index >= palette.Length / 3)
                        throw new InvalidDataException("The PNG uses an index outside its palette.");
                    red = palette[index * 3];
                    green = palette[index * 3 + 1];
                    blue = palette[index * 3 + 2];
                    if (transparency is not null && index < transparency.Length)
                        alpha = transparency[index];
                }
                else if (color is 0 or 4)
                {
                    int gray = ReadSample(row, x * channels, depth);
                    red = green = blue = ToByte(gray, depth);
                    if (color == 4)
                        alpha = ToByte(ReadSample(row, x * channels + 1, depth), depth);
                    else if (transparency is not null && gray == BinaryPrimitives.ReadUInt16BigEndian(transparency))
                        alpha = 0;
                }
                else
                {
                    int sample = x * channels;
                    int rawRed = ReadSample(row, sample, depth);
                    int rawGreen = ReadSample(row, sample + 1, depth);
                    int rawBlue = ReadSample(row, sample + 2, depth);
                    red = ToByte(rawRed, depth);
                    green = ToByte(rawGreen, depth);
                    blue = ToByte(rawBlue, depth);
                    if (color == 6)
                        alpha = ToByte(ReadSample(row, sample + 3, depth), depth);
                    else if (transparency is not null &&
                        rawRed == BinaryPrimitives.ReadUInt16BigEndian(transparency) &&
                        rawGreen == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2)) &&
                        rawBlue == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4)))
                        alpha = 0;
                }
                int destination = (y * width + x) * 4;
                pixels[destination] = (byte)((blue * alpha + 127) / 255);
                pixels[destination + 1] = (byte)((green * alpha + 127) / 255);
                pixels[destination + 2] = (byte)((red * alpha + 127) / 255);
                pixels[destination + 3] = (byte)alpha;
            }
            (previous, row) = (row, previous);
        }
        if (zlib.ReadByte() != -1)
            throw new InvalidDataException("The PNG has more image data than its dimensions permit.");
        return new PngImage(width, height, pixels);
    }

    private static int ReadSample(ReadOnlySpan<byte> row, int sample, int depth) => depth switch
    {
        16 => BinaryPrimitives.ReadUInt16BigEndian(row[(sample * 2)..]),
        8 => row[sample],
        _ => (row[sample * depth / 8] >> (8 - depth - sample * depth % 8)) & ((1 << depth) - 1)
    };

    private static int ToByte(int sample, int depth) => depth switch
    {
        16 => sample >> 8,
        8 => sample,
        _ => sample * 255 / ((1 << depth) - 1)
    };

    private static int Paeth(int left, int above, int upperLeft)
    {
        int prediction = left + above - upperLeft;
        int distanceLeft = Math.Abs(prediction - left);
        int distanceAbove = Math.Abs(prediction - above);
        int distanceUpperLeft = Math.Abs(prediction - upperLeft);
        return distanceLeft <= distanceAbove && distanceLeft <= distanceUpperLeft
            ? left : distanceAbove <= distanceUpperLeft ? above : upperLeft;
    }

    private static uint CalculateCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in type)
            crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        foreach (byte value in data)
            crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint crc = index;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            table[index] = crc;
        }
        return table;
    }
}
