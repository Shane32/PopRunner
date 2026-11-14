using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using PopRunner;

if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: PopRunner.ExtractAssets.Checks <extractor.dll> <existing-assets-directory> <test-output-directory> <game-directory> [game-directory ...]");
    return 2;
}

string executable = Path.GetFullPath(args[0]);
string baseline = Path.GetFullPath(args[1]);
string verificationRoot = Path.Combine(Path.GetFullPath(args[2]), "run-" + Guid.NewGuid().ToString("N"));
string[] sources = args[3..].Select(Path.GetFullPath).ToArray();
string[] dataFiles = ["KID.DAT", "SHADOW.DAT", "PV.DAT", "PRINCE.DAT"];
Directory.CreateDirectory(verificationRoot);
int comparisons = 0;
string? firstOutput = null;
for (int index = 0; index < sources.Length; index++)
{
    string destination = Path.Combine(verificationRoot, "source-" + (index + 1));
    var result = Run(sources[index], destination);
    Require(result.ExitCode == 0, $"Extraction failed for {sources[index]}: {result.StandardError}\n{result.StandardOutput}");
    Require(!result.StandardError.Contains("warning", StringComparison.OrdinalIgnoreCase),
        $"Known input unexpectedly warned: {sources[index]}: {result.StandardError}");
    CompareImages(baseline, destination, sources[index]);
    if (firstOutput is not null)
    {
        foreach (string filename in Directory.GetFiles(firstOutput, "*.png", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(firstOutput, filename);
            Require(File.ReadAllBytes(filename).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(destination, relative))),
                $"PNG file bytes differ between game inputs: {relative}");
        }
    }
    firstOutput ??= destination;
    Console.WriteLine($"PASS {sources[index]}: 318 image names, dimensions and exact RGBA pixels match existing assets; generated PNG bytes match previous sources.");
}

// Trailing container data changes its hash without changing any resource bytes.
string modifiedSource = Path.Combine(verificationRoot, "modified-input");
CopyDataFiles(sources[^1], modifiedSource);
using (var stream = new FileStream(Path.Combine(modifiedSource, "KID.DAT"), FileMode.Append))
    stream.WriteByte(0);
string modifiedDestination = Path.Combine(verificationRoot, "modified-output");
var modifiedResult = Run(modifiedSource, modifiedDestination);
Require(modifiedResult.ExitCode == 0, $"Unknown hash valid extraction failed: {modifiedResult.StandardError}");
Require(modifiedResult.StandardError.Contains("warning", StringComparison.OrdinalIgnoreCase)
    && modifiedResult.StandardError.Contains("KID.DAT", StringComparison.OrdinalIgnoreCase),
    "An unknown KID.DAT hash did not produce a warning on standard error.");
CompareImages(baseline, modifiedDestination, "unknown hash");
Console.WriteLine("PASS unknown hash: warns, succeeds, and produces the same 318 images.");

CheckMalformed("zero-sized-last-image", 172, (bytes, entryOffset, resourceOffset, size) =>
{
    bytes[resourceOffset + 1] = 0;
    bytes[resourceOffset + 2] = 0;
    RepairChecksum(bytes, resourceOffset, size);
});
CheckMalformed("truncated-compressed-image", 163, (bytes, entryOffset, resourceOffset, size) =>
{
    // Keep the complete image header, but remove all of its compressed payload
    // from the resource view and repair the shorter resource's checksum.
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(entryOffset + 6), 6);
    RepairChecksum(bytes, resourceOffset, 6);
});
Console.WriteLine($"PASS: {comparisons} exact RGBA image comparisons; {sources.Length} game inputs, deterministic PNGs, hash warning, and parse-before-save failures.");
Console.WriteLine($"Verification files: {verificationRoot}");
return 0;

(int ExitCode, string StandardOutput, string StandardError) Run(string source, string output)
{
    var start = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    start.ArgumentList.Add(executable);
    start.ArgumentList.Add(source);
    start.ArgumentList.Add(output);
    using var process = Process.Start(start) ?? throw new Exception("Could not launch extractor.");
    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
    Task<string> stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(30000))
    {
        process.Kill(entireProcessTree: true);
        throw new Exception("Extractor did not complete in 30 seconds.");
    }
    return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
}

void CompareImages(string expectedDirectory, string actualDirectory, string label)
{
    string[] expectedFiles = Directory.GetFiles(expectedDirectory, "*.png", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(expectedDirectory, path)).Order(StringComparer.Ordinal).ToArray();
    string[] actualFiles = Directory.GetFiles(actualDirectory, "*.png", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(actualDirectory, path)).Order(StringComparer.Ordinal).ToArray();
    Require(expectedFiles.Length == 318, "Expected baseline to contain 318 PNG files.");
    Require(expectedFiles.SequenceEqual(actualFiles), $"{label}: image names do not match the baseline.");
    foreach (string name in expectedFiles)
    {
        string expectedPath = Path.Combine(expectedDirectory, name);
        string actualPath = Path.Combine(actualDirectory, name);
        // The app's PNG reader independently verifies format, CRCs and pixels.
        using var expectedStream = File.OpenRead(expectedPath);
        using var actualStream = File.OpenRead(actualPath);
        PngImage expected = PngImage.Load(expectedStream);
        PngImage actual = PngImage.Load(actualStream);
        Require(expected.Width == actual.Width && expected.Height == actual.Height,
            $"{label}/{name}: image dimensions differ ({expected.Width}x{expected.Height} vs {actual.Width}x{actual.Height}).");
        Require(expected.Pixels.AsSpan().SequenceEqual(actual.Pixels), $"{label}/{name}: rendered pixels differ.");
        // Also retain RGB underneath alpha zero: the original potion PNGs use
        // transparent white, while the original character PNGs use black.
        Require(PngPixels.ReadRgba(expectedPath).AsSpan().SequenceEqual(PngPixels.ReadRgba(actualPath)),
            $"{label}/{name}: exact RGBA bytes differ, including transparent pixel RGB.");
        comparisons++;
    }
}

void CopyDataFiles(string source, string output)
{
    Directory.CreateDirectory(output);
    foreach (string file in dataFiles)
        File.Copy(Path.Combine(source, file), Path.Combine(output, file));
}

void CheckMalformed(string name, int resourceId, Action<byte[], int, int, int> mutate)
{
    string source = Path.Combine(verificationRoot, name + "-input");
    CopyDataFiles(sources[^1], source);
    string filename = Path.Combine(source, "PRINCE.DAT");
    byte[] bytes = File.ReadAllBytes(filename);
    int tableOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes));
    int resourceCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(tableOffset));
    bool corrupted = false;
    for (int index = 0; index < resourceCount; index++)
    {
        int entryOffset = tableOffset + 2 + index * 8;
        ReadOnlySpan<byte> entry = bytes.AsSpan(entryOffset, 8);
        if (BinaryPrimitives.ReadUInt16LittleEndian(entry) != resourceId)
            continue;
        int offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(entry[2..]));
        int size = BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]);
        mutate(bytes, entryOffset, offset, size);
        corrupted = true;
        break;
    }
    Require(corrupted, $"Could not locate resource {resourceId} for malformed input.");
    File.WriteAllBytes(filename, bytes);
    string existingDestination = Path.Combine(verificationRoot, name + "-existing-output");
    Directory.CreateDirectory(Path.Combine(existingDestination, "Prince"));
    File.WriteAllText(Path.Combine(existingDestination, "sentinel.txt"), "Preserve this unrelated file.");
    File.WriteAllText(Path.Combine(existingDestination, "Prince", "res401.png"), "Preserve this existing image on parse failure.");
    var before = Snapshot(existingDestination);
    var result = Run(source, existingDestination);
    Require(result.ExitCode == 1, $"{name}: expected clean error exit 1, got {result.ExitCode}: {result.StandardError}");
    Require(result.StandardError.Contains("Error:", StringComparison.Ordinal), $"{name}: missing friendly error diagnostic.");
    Require(before.OrderBy(pair => pair.Key).SequenceEqual(Snapshot(existingDestination).OrderBy(pair => pair.Key)),
        $"{name}: a late parsing error modified or created files/directories in an existing destination.");
    string absentDestination = Path.Combine(verificationRoot, name + "-absent-output");
    var absentResult = Run(source, absentDestination);
    Require(absentResult.ExitCode == 1, $"{name}: malformed image unexpectedly succeeded for absent output.");
    Require(!Directory.Exists(absentDestination), $"{name}: parsing error created the destination directory.");
    Console.WriteLine($"PASS {name}: existing destination unchanged and absent destination not created.");
}

Dictionary<string, string> Snapshot(string directory)
{
    var result = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .ToDictionary(path => "file:" + Path.GetRelativePath(directory, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    foreach (string path in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories))
        result.Add("directory:" + Path.GetRelativePath(directory, path), "");
    return result;
}

static void RepairChecksum(byte[] bytes, int offset, int size)
{
    int sum = 0;
    foreach (byte value in bytes.AsSpan(offset + 1, size))
        sum += value;
    bytes[offset] = unchecked((byte)(255 - sum));
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}
