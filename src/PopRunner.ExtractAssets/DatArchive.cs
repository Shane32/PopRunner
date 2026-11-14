using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PopRunner.ExtractAssets;

/// <summary>Reads a DOS PoP1 DAT directory and verifies resource bounds and checksums.</summary>
internal sealed class DatArchive
{
    private readonly byte[] bytes;
    private readonly Dictionary<int, (int Offset, int Size)> resources = [];

    internal string Name { get; }
    internal string Sha256 { get; }

    internal DatArchive(string path, Action<string> warn, params string[] expectedHashes)
    {
        Name = Path.GetFileName(path);
        using (var stream = File.OpenRead(path))
        {
            if (stream.Length is < 6 or > 16 * 1024 * 1024)
                throw new InvalidDataException($"{Name}: invalid DAT file size.");
            bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
        }
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        if (!expectedHashes.Contains(Sha256, StringComparer.OrdinalIgnoreCase))
            warn($"{Name}: unexpected SHA-256 {Sha256}; expected {string.Join(" or ", expectedHashes)}. Attempting to parse the data.");

        var tableOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        var tableSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
        if (tableOffset < 6 || tableSize < 2 || (long)tableOffset + tableSize > bytes.Length)
            throw new InvalidDataException($"{Name}: invalid resource directory.");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan((int)tableOffset));
        if (2L + count * 8L > tableSize)
            throw new InvalidDataException($"{Name}: truncated resource directory.");
        for (var index = 0; index < count; index++)
        {
            var entry = bytes.AsSpan((int)tableOffset + 2 + index * 8, 8);
            var id = BinaryPrimitives.ReadUInt16LittleEndian(entry);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[2..]);
            var size = BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]);
            if (offset < 6 || (long)offset + 1 + size > tableOffset)
                throw new InvalidDataException($"{Name}: resource {id} is outside the data area.");
            if (!resources.TryAdd(id, ((int)offset, size)))
                throw new InvalidDataException($"{Name}: duplicate resource {id}.");
        }
    }

    internal ReadOnlySpan<byte> Read(int id)
    {
        if (!resources.TryGetValue(id, out var record))
            throw new InvalidDataException($"{Name}: resource {id} is missing.");
        var stored = bytes.AsSpan(record.Offset, record.Size + 1);
        var sum = 0;
        foreach (var value in stored) sum += value;
        if ((sum & 255) != 255)
            throw new InvalidDataException($"{Name}: resource {id} has an invalid checksum.");
        return stored[1..];
    }
}
