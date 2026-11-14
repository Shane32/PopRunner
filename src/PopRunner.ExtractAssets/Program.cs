namespace PopRunner.ExtractAssets;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            Usage(Console.Out);
            return 0;
        }
        if (args.Length != 2)
        {
            Usage(Console.Error);
            return 2;
        }
        try
        {
            var source = Path.GetFullPath(args[0]);
            var output = Path.GetFullPath(args[1]);
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Game directory not found: {source}");
            Console.WriteLine($"Reading DOS Prince of Persia assets from {source}");
            // Decode every required image, verify its resource, and encode every PNG before
            // creating directories or opening any destination file. Parse errors leave output untouched.
            var prepared = AssetExtractor.Prepare(source, warning => Console.Error.WriteLine($"Warning: {warning}"));
            Console.WriteLine("Prepared 318 images (251 Prince, 59 Princess, 8 Potion). Saving...");
            AssetExtractor.Save(prepared, output);
            Console.WriteLine($"Saved {prepared.Count} PNGs to {output}");
            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine($"Error: {error.Message}");
            return 1;
        }
    }

    private static void Usage(TextWriter writer)
    {
        writer.WriteLine("Usage: PopRunner.ExtractAssets <game-directory> <output-assets-directory>");
        writer.WriteLine("Extracts DOS Prince of Persia 1.0, 1.1, 1.3 or 1.4 assets from KID.DAT, SHADOW.DAT, PV.DAT and PRINCE.DAT.");
        writer.WriteLine("Writes Prince/, Princess/ and Potion/ under the output directory; existing named outputs are overwritten.");
    }
}
