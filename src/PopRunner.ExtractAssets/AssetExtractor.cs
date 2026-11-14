namespace PopRunner.ExtractAssets;

internal sealed record PreparedFile(string RelativePath, byte[] Bytes);

internal static class AssetExtractor
{
    internal static IReadOnlyList<PreparedFile> Prepare(string sourceDirectory, Action<string> warn)
    {
        var kid = Open("KID.DAT", "2EAA798041C090A6D54CD0DCE4AE770E3DA400EF95540CEC7ED0FF9DB5C2AF73");
        var shadow = Open("SHADOW.DAT", "5B6DED9399D13C3A877FF9EA003CBABDB222BBC1AE4A0FAC81EB4913340BF9AE");
        var pv = Open("PV.DAT", "418BE33393E8AE25B724670D74F9B1FD6F1FD2EEF38639E2F679701DDB8A4DF2");
        var prince = Open("PRINCE.DAT",
            "28D8B3A8C48ED6B7B280B16BCB3C2E1AC446F2A210A508D85937FBEB2196BF48",
            "0EC31EF20C8253530728E40FE78531FEE94CB1265C6E6F685C06E6D116E595D8");

        var files = new List<PreparedFile>(318);
        AddRange(kid, 400, "Prince", Enumerable.Range(401, 219));
        AddRange(shadow, 750, "Prince", Enumerable.Range(751, 32));
        AddRange(pv, 800, "Princess", Enumerable.Range(801, 17));
        AddRange(pv, 900, "Princess", new[] { 901 }.Concat(Enumerable.Range(903, 28)));
        AddRange(pv, 950, "Princess", Enumerable.Range(951, 12));
        AddRange(pv, 980, "Princess", new[] { 981 });

        var potionPalette = DatImageDecoder.ReadPalette(prince, 150);
        // Preserve the transparent-white RGB stored by the existing potion exporter.
        potionPalette.AsSpan(0, 3).Fill(255);
        byte[] bubblePalette = [255, 255, 255, 0, 224, 0, 48, 255];
        AddPotion(163, "res163.png", potionPalette);
        for (var id = 166; id <= 172; id++) AddPotion(id, $"res{id}-red.png", bubblePalette);
        return files;

        DatArchive Open(string name, params string[] hashes)
        {
            var matches = Directory.EnumerateFiles(sourceDirectory)
                .Where(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Expected exactly one {name} in '{sourceDirectory}'; found {matches.Length}.");
            return new DatArchive(matches[0], warn, hashes);
        }

        void AddRange(DatArchive archive, int paletteId, string folder, IEnumerable<int> ids)
        {
            var palette = DatImageDecoder.ReadPalette(archive, paletteId);
            foreach (var id in ids)
            {
                var image = DatImageDecoder.Decode(archive, id, palette);
                files.Add(new PreparedFile(Path.Combine(folder, $"res{id}.png"),
                    PngEncoder.Encode(image.Width, image.Height, image.Rgba)));
            }
        }

        void AddPotion(int id, string name, byte[] palette)
        {
            var image = DatImageDecoder.Decode(prince, id, palette);
            files.Add(new PreparedFile(Path.Combine("Potion", name), PngEncoder.Encode(image.Width, image.Height, image.Rgba)));
        }
    }

    internal static void Save(IReadOnlyList<PreparedFile> files, string outputDirectory)
    {
        foreach (var file in files)
        {
            var target = Path.Combine(outputDirectory, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, file.Bytes);
        }
    }
}
