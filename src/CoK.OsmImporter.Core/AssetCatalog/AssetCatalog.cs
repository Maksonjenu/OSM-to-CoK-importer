using System.Reflection;
using System.Text.Json.Serialization;

namespace CoK.OsmImporter.Core.AssetCatalog;

internal sealed class CatalogObjectEntry
{
    [JsonPropertyName("filename")]
    public required string Filename { get; init; }

    [JsonPropertyName("object_type")]
    public required int ObjectType { get; init; }
}

internal sealed class CatalogLineTileEntry
{
    [JsonPropertyName("filename")]
    public required string Filename { get; init; }

    [JsonPropertyName("object_type")]
    public required int ObjectType { get; init; }
}

internal sealed class CatalogPathEntry
{
    [JsonPropertyName("filename")]
    public required string Filename { get; init; }

    [JsonPropertyName("path_type")]
    public required int PathType { get; init; }

    [JsonPropertyName("is_closed")]
    public bool IsClosed { get; init; }

    /// <summary>
    /// The prefab CoK stretches along each straight segment to actually render this path/plot
    /// variant, if it uses that pattern (roads, stone walls, palisades, fences). Null for
    /// features CoK renders procedurally from the spline/polygon alone (rivers, forest/water/
    /// farmland plots) or that use a more complex scatter (hedges) not modeled in v1.
    /// </summary>
    [JsonPropertyName("line_tile")]
    public CatalogLineTileEntry? LineTile { get; init; }
}

internal sealed class CatalogRoot
{
    [JsonPropertyName("objects")]
    public List<CatalogObjectEntry> Objects { get; init; } = new();

    [JsonPropertyName("paths")]
    public List<CatalogPathEntry> Paths { get; init; } = new();
}

public readonly record struct LineTile(string Filename, int ObjectType);

public readonly record struct PathAsset(string Filename, int PathType, bool IsClosed, LineTile? LineTile);

/// <summary>
/// The set of `.tscn` prefabs known to exist in Canvas of Kings, extracted once from
/// `template.mommap` (a `filename` always implies exactly one `object_type` for point objects;
/// a `(filename, path_type)` pair identifies one path/plot variant). Used to fail fast — with a
/// clear error — if `mapping.json` references an asset that doesn't actually exist, instead of
/// silently writing a `.mommap` the game can't resolve.
/// </summary>
public sealed class AssetCatalog
{
    private readonly Dictionary<string, int> _objectTypesByFilename;
    private readonly Dictionary<(string Filename, int PathType), PathAsset> _paths;

    private AssetCatalog(Dictionary<string, int> objectTypes, Dictionary<(string, int), PathAsset> paths)
    {
        _objectTypesByFilename = objectTypes;
        _paths = paths;
    }

    public static AssetCatalog LoadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "CoK.OsmImporter.Core.AssetCatalog.asset_catalog.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        var root = System.Text.Json.JsonSerializer.Deserialize<CatalogRoot>(stream)
            ?? throw new InvalidOperationException("Embedded asset catalog failed to deserialize.");

        var objectTypes = root.Objects.ToDictionary(o => o.Filename, o => o.ObjectType);
        var paths = root.Paths.ToDictionary(
            p => (p.Filename, p.PathType),
            p => new PathAsset(
                p.Filename, p.PathType, p.IsClosed,
                p.LineTile is { } t ? new LineTile(t.Filename, t.ObjectType) : null));

        return new AssetCatalog(objectTypes, paths);
    }

    public int GetObjectType(string filename) =>
        _objectTypesByFilename.TryGetValue(filename, out var type)
            ? type
            : throw new AssetNotFoundException(
                $"Unknown object prefab '{filename}' — not present in the asset catalog extracted from template.mommap. " +
                "Check for typos in mapping.json, or re-extract the catalog if CoK added new prefabs.");

    public PathAsset GetPath(string filename, int pathType) =>
        _paths.TryGetValue((filename, pathType), out var asset)
            ? asset
            : throw new AssetNotFoundException(
                $"Unknown path/plot asset '{filename}' with path_type={pathType} — not present in the asset catalog. " +
                "Check for typos in mapping.json, or re-extract the catalog if CoK added new prefabs.");

    public bool TryGetPath(string filename, int pathType, out PathAsset asset) =>
        _paths.TryGetValue((filename, pathType), out asset);

    public IReadOnlyCollection<string> ObjectFilenames => _objectTypesByFilename.Keys;
    public IReadOnlyCollection<PathAsset> PathAssets => _paths.Values;
}

public sealed class AssetNotFoundException(string message) : Exception(message);
