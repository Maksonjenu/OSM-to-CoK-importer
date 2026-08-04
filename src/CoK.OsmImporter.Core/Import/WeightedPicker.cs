using CoK.OsmImporter.Core.Mapping;

namespace CoK.OsmImporter.Core.Import;

/// <summary>
/// Deterministic pseudo-random choices seeded by the source OSM element id, so re-running the
/// importer on the same input (and mapping config) always produces the same output — no surprise
/// diffs between runs.
/// </summary>
internal static class WeightedPicker
{
    public static string PickAsset(IReadOnlyList<WeightedAsset> assets, long osmId, int salt)
    {
        if (assets.Count == 0)
            throw new InvalidOperationException("Cannot pick from an empty asset list — check mapping.json.");
        if (assets.Count == 1)
            return assets[0].Filename;

        var rng = new Random(Seed(osmId, salt));
        var total = assets.Sum(a => a.Weight);
        var r = rng.NextDouble() * total;
        var cumulative = 0.0;
        foreach (var asset in assets)
        {
            cumulative += asset.Weight;
            if (r <= cumulative)
                return asset.Filename;
        }

        return assets[^1].Filename;
    }

    public static double JitterInRange(long osmId, int salt, double min, double max)
    {
        var rng = new Random(Seed(osmId, salt));
        return min + rng.NextDouble() * (max - min);
    }

    private static int Seed(long osmId, int salt)
    {
        var combined = unchecked((ulong)osmId) * 2654435761UL + (uint)salt;
        return unchecked((int)combined);
    }
}
