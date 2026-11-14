using System.Reflection;

namespace PopRunner.Core;

/// <summary>The configuration and original sprite images packaged with the animation engine.</summary>
public static class AnimationResources
{
    private const string ResourcePrefix = "PopRunner.Core.";
    private static readonly Assembly ResourceAssembly = typeof(AnimationResources).Assembly;
    private static readonly Dictionary<string, string> ImageResourceNames = FindImages();

    /// <summary>All embedded PNG paths, using the same paths as the animation configuration.</summary>
    public static IReadOnlyList<string> ImagePaths { get; } =
        Array.AsReadOnly(ImageResourceNames.Keys.Order(StringComparer.Ordinal).ToArray());

    /// <summary>Opens the packaged default JSON. The caller owns the returned stream.</summary>
    public static Stream OpenConfiguration() => OpenResource(ResourcePrefix + "animation.json");

    /// <summary>Opens an original PNG. The caller owns the returned stream.</summary>
    public static Stream OpenImage(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ImageResourceNames.TryGetValue(NormalizePath(path), out var resourceName)
            ? OpenResource(resourceName)
            : throw new InvalidDataException($"Missing embedded image {path}.");
    }

    public static bool ContainsImage(string path) =>
        path is not null && ImageResourceNames.ContainsKey(NormalizePath(path));

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static Stream OpenResource(string name) =>
        ResourceAssembly.GetManifestResourceStream(name)
        ?? throw new InvalidDataException($"Missing embedded resource {name}.");

    private static Dictionary<string, string> FindImages()
    {
        const string assetsPrefix = ResourcePrefix + "Assets.";
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in ResourceAssembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(assetsPrefix, StringComparison.Ordinal) ||
                !name.EndsWith(".png", StringComparison.Ordinal)) continue;

            // Each asset collection is a folder; keep the filename (including any dots) intact.
            var relativeName = name[assetsPrefix.Length..];
            var folderEnd = relativeName.IndexOf('.');
            if (folderEnd <= 0) throw new InvalidDataException($"Invalid embedded image resource {name}.");
            var path = "Assets/" + relativeName[..folderEnd] + "/" + relativeName[(folderEnd + 1)..];
            images.Add(path, name);
        }
        return images;
    }
}
