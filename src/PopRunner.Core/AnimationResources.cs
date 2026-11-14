using System.Reflection;

namespace PopRunner.Core;

/// <summary>The configuration and original sprite images packaged with the animation engine.</summary>
public static class AnimationResources
{
    private const string ResourcePrefix = "PopRunner.Core.";
    private static readonly Assembly ResourceAssembly = typeof(AnimationResources).Assembly;
    private static readonly Dictionary<string, string> ImageResourceNames = FindImages();
    private static readonly Dictionary<string, string?> ImageFileNames = FindImageFileNames();

    /// <summary>All embedded PNG paths, using the same paths as the animation configuration.</summary>
    public static IReadOnlyList<string> ImagePaths { get; } =
        Array.AsReadOnly(ImageResourceNames.Keys.Order(StringComparer.Ordinal).ToArray());

    /// <summary>Opens the packaged default JSON. The caller owns the returned stream.</summary>
    public static Stream OpenConfiguration() => OpenResource(ResourcePrefix + "animation.json");

    /// <summary>Opens an original PNG. The caller owns the returned stream.</summary>
    public static Stream OpenImage(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return OpenResource(ImageResourceNames[ResolveImagePath(path)]);
    }

    public static bool ContainsImage(string path) => TryResolveImagePath(path, out _);

    /// <summary>Resolves a logical image path or unique filename to its canonical embedded path.</summary>
    public static string ResolveImagePath(string path)
    {
        if (TryResolveImagePath(path, out var resolved)) return resolved;
        if (path is not null && !NormalizePath(path).Contains('/') && ImageFileNames.ContainsKey(path))
            throw new InvalidDataException($"Ambiguous embedded image filename {path}; use its full Assets path.");
        throw new InvalidDataException($"Missing embedded image {path}.");
    }

    private static bool TryResolveImagePath(string? path, out string resolved)
    {
        resolved = "";
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = NormalizePath(path);
        if (ImageResourceNames.ContainsKey(normalized))
        {
            resolved = normalized;
            return true;
        }
        if (!normalized.Contains('/') && ImageFileNames.TryGetValue(normalized, out var fullPath) && fullPath is not null)
        {
            resolved = fullPath;
            return true;
        }
        return false;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static Dictionary<string, string?> FindImageFileNames()
    {
        var paths = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in ImageResourceNames.Keys)
        {
            var name = path[(path.LastIndexOf('/') + 1)..];
            if (!paths.TryAdd(name, path)) paths[name] = null;
        }
        return paths;
    }

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
