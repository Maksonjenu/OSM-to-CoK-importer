using System.Text.Json.Serialization;

namespace CoK.OsmImporter.Core.Mapping;

public sealed class WeightedAsset
{
    [JsonPropertyName("filename")]
    public required string Filename { get; init; }

    [JsonPropertyName("weight")]
    public double Weight { get; init; } = 1.0;
}

/// <summary>A single path/plot prefab + variant, e.g. the river spline or the forest plot.</summary>
public sealed class PathAssetRule
{
    [JsonPropertyName("filename")]
    public required string Filename { get; init; }

    [JsonPropertyName("path_type")]
    public required int PathType { get; init; }

    /// <summary>
    /// Spline width in map units, applied to every vertex (see MapPath.PointObject.ScaleCustomX).
    /// Only meaningful for width-via-spline features (currently just rivers) — ignored otherwise.
    /// </summary>
    [JsonPropertyName("width")]
    public double? Width { get; init; }
}

/// <summary>highway=* value -> which path_type of the shared road prefab to use.</summary>
public sealed class RoadRule
{
    [JsonPropertyName("path_type")]
    public required int PathType { get; init; }
}

/// <summary>
/// One entry in an ordered building-prefab rule list: "if the way has tag `TagKey`=`TagValue`
/// (or `TagValue` is `*`, meaning any value), place a weighted-random pick from `Assets`."
/// Rules are evaluated in file order, first match wins; <see cref="BuildingMapping.Default"/> is
/// used when nothing matches (which, since building=* is how a way became a "building" feature in
/// the first place, is always at least "some kind of building").
/// </summary>
public sealed class BuildingRule
{
    [JsonPropertyName("tag_key")]
    public required string TagKey { get; init; }

    [JsonPropertyName("tag_value")]
    public required string TagValue { get; init; }

    [JsonPropertyName("assets")]
    public required List<WeightedAsset> Assets { get; init; }
}

public sealed class BuildingMapping
{
    [JsonPropertyName("rules")]
    public List<BuildingRule> Rules { get; init; } = new();

    [JsonPropertyName("default")]
    public List<WeightedAsset> Default { get; init; } = new();
}

/// <summary>
/// natural=/landuse=/waterway= driven area or line features that all resolve to a single fixed
/// path/plot asset (as opposed to roads and buildings, which pick between several).
/// </summary>
public sealed class TagDrivenPathRule
{
    [JsonPropertyName("asset")]
    public required PathAssetRule Asset { get; init; }

    [JsonPropertyName("natural_values")]
    public List<string> NaturalValues { get; init; } = new();

    [JsonPropertyName("landuse_values")]
    public List<string> LanduseValues { get; init; } = new();

    [JsonPropertyName("waterway_values")]
    public List<string> WaterwayValues { get; init; } = new();

    /// <summary>
    /// Weighted-random interior fill for closed Plot types (forest). CoK bakes actual object
    /// instances into a plot's interior when it's drawn in the editor — it's not procedural at
    /// load time — so an importer-created plot needs the same treatment or it renders empty. See
    /// MapPath.AreaObjects. Empty/unused for line features (waterway) and plots whose fill is a
    /// shader/mesh effect rather than discrete objects (water_polygon).
    /// </summary>
    [JsonPropertyName("fill")]
    public List<WeightedAsset> Fill { get; init; } = new();

    /// <summary>Fill objects per output map unit² (NOT scaled by --scale — same reasoning as
    /// ConverterOptions.SimplificationUnits: this is about what looks/performs reasonably in the
    /// final output, not real-world tree density).</summary>
    [JsonPropertyName("fill_density")]
    public double? FillDensity { get; init; }
}

/// <summary>
/// The full set of user-tunable OSM-tag -> CoK-prefab rules. Ships with a documented-guess
/// default (see default-mapping.json) since nobody but someone with the CoK editor open can know
/// what each `path_type`/prefab actually looks like in-game — this file is meant to be hand-edited.
/// </summary>
public sealed class MappingConfig
{
    [JsonPropertyName("road_filename")]
    public required string RoadFilename { get; init; }

    /// <summary>highway=value -> path_type. A "default" key is used for unrecognized highway values.</summary>
    [JsonPropertyName("roads")]
    public Dictionary<string, RoadRule> Roads { get; init; } = new();

    /// <summary>
    /// A way classified as a Road that also carries a truthy `bridge` tag (`bridge=yes`,
    /// `viaduct`, ... — anything except absent/`no`) uses this fixed path asset instead of
    /// road_filename/the highway-hierarchy path_type. CoK's bridge prefabs (pa_bridge_1/2.tscn) are
    /// completely different filenames from pa_road.tscn, not just another path_type variant of it
    /// — same reason barriers get their own PathAssetRule instead of reusing the road prefab.
    /// Optional: null (the default for a custom mapping.json predating this field) just means
    /// bridges render as a plain road, same as before this was added.
    /// </summary>
    [JsonPropertyName("bridge")]
    public PathAssetRule? Bridge { get; init; }

    /// <summary>barrier=value -> fixed path asset (hedge/wall/fence/city wall...).</summary>
    [JsonPropertyName("barriers")]
    public Dictionary<string, PathAssetRule> Barriers { get; init; } = new();

    [JsonPropertyName("waterway")]
    public required TagDrivenPathRule Waterway { get; init; }

    [JsonPropertyName("water_polygon")]
    public required TagDrivenPathRule WaterPolygon { get; init; }

    [JsonPropertyName("forest_polygon")]
    public required TagDrivenPathRule ForestPolygon { get; init; }

    [JsonPropertyName("farmland_polygon")]
    public required TagDrivenPathRule FarmlandPolygon { get; init; }

    [JsonPropertyName("trees")]
    public List<WeightedAsset> Trees { get; init; } = new();

    [JsonPropertyName("buildings")]
    public BuildingMapping Buildings { get; init; } = new();
}
