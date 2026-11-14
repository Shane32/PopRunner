using System.Buffers.Binary;
using PopRunner.Core;

namespace PopRunner.Checks;

internal static class ResourceChecks
{
    public static void Run()
    {
        Check.Run("Resources: embedded image inventory is nonempty and unique", () =>
        {
            var paths = AnimationResources.ImagePaths;
            Check.That(paths.Count > 0, "Core must contain images.");
            Check.That(paths.Distinct(StringComparer.Ordinal).Count() == paths.Count, "Embedded image paths must be unique.");
        });

        Check.Run("Resources: all embedded images have PNG headers and positive dimensions", () =>
        {
            ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
            Span<byte> header = stackalloc byte[24];
            foreach (var path in AnimationResources.ImagePaths)
            {
                using var stream = AnimationResources.OpenImage(path);
                stream.ReadExactly(header);
                Check.That(header[..8].SequenceEqual(pngSignature), $"Embedded {path} must have a PNG signature.");
                Check.That(header[12..16].SequenceEqual("IHDR"u8), $"Embedded {path} must begin with its PNG dimensions.");
                Check.That(BinaryPrimitives.ReadInt32BigEndian(header[16..20]) > 0 &&
                    BinaryPrimitives.ReadInt32BigEndian(header[20..24]) > 0,
                    $"Embedded {path} must have positive dimensions.");
            }
        });

        Check.Run("Resources: image lookup accepts Windows path separators", () =>
        {
            var path = AnimationResources.ImagePaths.First();
            var windowsPath = path.Replace('/', '\\');
            Check.That(AnimationResources.ContainsImage(windowsPath), "Windows separators must resolve the same embedded image.");
            using var original = AnimationResources.OpenImage(path);
            using var alternate = AnimationResources.OpenImage(windowsPath);
            using var originalBytes = new MemoryStream();
            using var alternateBytes = new MemoryStream();
            original.CopyTo(originalBytes);
            alternate.CopyTo(alternateBytes);
            Check.That(originalBytes.ToArray().AsSpan().SequenceEqual(alternateBytes.ToArray()),
                "Both path forms must open identical image bytes.");
        });

        Check.Run("Resources: absent images and traversal paths are rejected", () =>
        {
            var path = AnimationResources.ImagePaths.First();
            foreach (var invalidPath in new[] { path + ".missing", "../" + path, "..\\" + path, "" })
            {
                Check.That(!AnimationResources.ContainsImage(invalidPath), $"Image lookup must reject '{invalidPath}'.");
                if (invalidPath.Length == 0)
                    Check.Throws<ArgumentException>(() => AnimationResources.OpenImage(invalidPath), "An empty image path must be rejected.");
                else
                    Check.Throws<InvalidDataException>(() => AnimationResources.OpenImage(invalidPath), $"Opening '{invalidPath}' must fail.");
            }
            Check.That(!AnimationResources.ContainsImage(null!), "Null must not name an embedded image.");
        });

        Check.Run("Resources: embedded default configuration loads and validates", () =>
        {
            // Loading validates resource references and completion without freezing any animation choices.
            using var stream = AnimationResources.OpenConfiguration();
            var loaded = AnimationConfig.Load(stream);
            Check.That(loaded.Scenarios.Count > 0, "The packaged configuration must provide a playable scenario.");
            Check.That(AnimationConfig.Load().Sprites.Count > 0, "The default load overload must open the packaged configuration.");
        });
    }
}
