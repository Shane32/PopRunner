using System.Buffers.Binary;

namespace PopRunner.ExtractAssets;

internal sealed record DecodedImage(int Width, int Height, byte[] Rgba);

/// <summary>Decodes the DOS indexed image format; all output pixels are straight RGBA.</summary>
internal static class DatImageDecoder
{
    internal static byte[] ReadPalette(DatArchive archive, int id)
    {
        var resource = archive.Read(id);
        if (resource.Length < 4 + 16 * 3)
            throw new InvalidDataException($"{archive.Name}: palette {id} is truncated.");
        var colors = new byte[16 * 4];
        for (var index = 0; index < 16; index++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                var component = resource[4 + index * 3 + channel];
                if (component > 63)
                    throw new InvalidDataException($"{archive.Name}: palette {id} has a non-VGA color component.");
                colors[index * 4 + channel] = (byte)(component * 4);
            }
            colors[index * 4 + 3] = index == 0 ? (byte)0 : (byte)255;
        }
        colors.AsSpan(0, 4).Clear();
        return colors;
    }

    internal static DecodedImage Decode(DatArchive archive, int id, ReadOnlySpan<byte> palette)
    {
        try { return Decode(archive.Read(id), palette); }
        catch (InvalidDataException error)
        {
            throw new InvalidDataException($"{archive.Name}, image {id}: {error.Message}", error);
        }
    }

    private static DecodedImage Decode(ReadOnlySpan<byte> resource, ReadOnlySpan<byte> palette)
    {
        if (resource.Length < 6) throw new InvalidDataException("Truncated image header.");
        var height = BinaryPrimitives.ReadUInt16LittleEndian(resource);
        var width = BinaryPrimitives.ReadUInt16LittleEndian(resource[2..]);
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(resource[4..]);
        var depth = ((flags >> 12) & 7) + 1;
        var method = (flags >> 8) & 15;
        if (depth is not (1 or 4) || method > 4)
            throw new InvalidDataException($"Unsupported image depth {depth} or compression {method}.");
        if (width is 0 or > 1024 || height is 0 or > 1024)
            throw new InvalidDataException("Invalid image dimensions (expected 1 through 1024 pixels).");
        if (palette.Length < (1 << depth) * 4)
            throw new InvalidDataException("The image palette is too short.");
        var stride = (width * depth + 7) / 8;
        var packed = Unpack(resource[6..], stride * height, method);
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var bit = x * depth;
                // Methods 2 and 4 emit bytes down columns instead of across rows.
                var packedIndex = method is 2 or 4 ? (bit / 8) * height + y : y * stride + bit / 8;
                var index = (packed[packedIndex] >> (8 - depth - bit % 8)) & ((1 << depth) - 1);
                palette.Slice(index * 4, 4).CopyTo(rgba.AsSpan((y * width + x) * 4));
            }
        }
        return new DecodedImage(width, height, rgba);
    }

    private static byte[] Unpack(ReadOnlySpan<byte> source, int size, int method)
    {
        var result = new byte[size];
        if (method == 0)
        {
            if (source.Length < size) throw new InvalidDataException("Truncated raw image.");
            source[..size].CopyTo(result);
            return result;
        }

        var input = 0;
        var output = 0;
        if (method is 1 or 2)
        {
            while (output < size)
            {
                if (input >= source.Length) throw new InvalidDataException("Truncated RLE control byte.");
                var control = (sbyte)source[input++];
                var count = control >= 0 ? control + 1 : -control;
                if (control >= 0)
                {
                    if (count > source.Length - input) throw new InvalidDataException("Truncated RLE literals.");
                    var take = Math.Min(count, size - output);
                    source.Slice(input, take).CopyTo(result.AsSpan(output));
                    input += count;
                    output += take;
                }
                else
                {
                    if (input >= source.Length) throw new InvalidDataException("Truncated RLE repeat.");
                    var take = Math.Min(count, size - output);
                    result.AsSpan(output, take).Fill(source[input++]);
                    output += take;
                }
            }
            return result;
        }

        var history = new byte[1024];
        var writeSlot = 958;
        while (output < size)
        {
            if (input >= source.Length) throw new InvalidDataException("Truncated LZSS flags.");
            var flags = source[input++];
            for (var bit = 0; bit < 8 && output < size; bit++)
            {
                if (input >= source.Length) throw new InvalidDataException("Truncated LZSS token.");
                if ((flags & (1 << bit)) != 0)
                {
                    var value = source[input++];
                    result[output++] = value;
                    history[writeSlot] = value;
                    writeSlot = (writeSlot + 1) & 1023;
                }
                else
                {
                    if (source.Length - input < 2) throw new InvalidDataException("Truncated LZSS reference.");
                    var token = (source[input] << 8) | source[input + 1];
                    input += 2;
                    var readSlot = token & 1023;
                    var count = Math.Min((token >> 10) + 3, size - output);
                    for (var copy = 0; copy < count; copy++)
                    {
                        var value = history[readSlot];
                        result[output++] = value;
                        history[writeSlot] = value;
                        readSlot = (readSlot + 1) & 1023;
                        writeSlot = (writeSlot + 1) & 1023;
                    }
                }
            }
        }
        return result;
    }
}
