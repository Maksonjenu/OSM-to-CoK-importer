using CoK.OsmImporter.Core.Mapping;

namespace CoK.OsmImporter.Core.Import;

public enum WayFeatureKind
{
    None,
    Waterway,
    WaterPolygon,
    ForestPolygon,
    FarmlandPolygon,
    Barrier,
    Building,
    Road,
}

/// <summary>
/// Decides which CoK feature category a tagged OSM way belongs to, in priority order. Only
/// depends on tags + closed-ness, so it's testable without touching geometry/projection at all.
/// Priority: waterway line &gt; water/forest/farmland polygon (needs a closed ring) &gt; barrier
/// line &gt; building &gt; generic road line. A way can only ever match one category — real-world
/// data essentially never combines these tags, so simple first-match-wins is fine for v1.
/// </summary>
public sealed class WayClassifier
{
    private readonly MappingConfig _mapping;

    public WayClassifier(MappingConfig mapping) => _mapping = mapping;

    public WayFeatureKind Classify(IReadOnlyDictionary<string, string> tags, bool isClosed)
    {
        if (tags.TryGetValue("waterway", out var waterwayValue)
            && _mapping.Waterway.WaterwayValues.Contains(waterwayValue))
            return WayFeatureKind.Waterway;

        if (isClosed && MatchesPolygonRule(tags, _mapping.WaterPolygon))
            return WayFeatureKind.WaterPolygon;

        if (isClosed && MatchesPolygonRule(tags, _mapping.ForestPolygon))
            return WayFeatureKind.ForestPolygon;

        if (isClosed && MatchesPolygonRule(tags, _mapping.FarmlandPolygon))
            return WayFeatureKind.FarmlandPolygon;

        if (tags.TryGetValue("barrier", out var barrierValue) && _mapping.Barriers.ContainsKey(barrierValue))
            return WayFeatureKind.Barrier;

        if (tags.ContainsKey("building"))
            return WayFeatureKind.Building;

        if (tags.ContainsKey("highway"))
            return WayFeatureKind.Road;

        return WayFeatureKind.None;
    }

    private static bool MatchesPolygonRule(IReadOnlyDictionary<string, string> tags, TagDrivenPathRule rule) =>
        (tags.TryGetValue("natural", out var naturalValue) && rule.NaturalValues.Contains(naturalValue))
        || (tags.TryGetValue("landuse", out var landuseValue) && rule.LanduseValues.Contains(landuseValue));
}
