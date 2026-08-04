using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoK.OsmImporter.Core.Mommap;

public sealed class PathPoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }

    public PathPoint() { }

    public PathPoint(double x, double z)
    {
        X = x;
        Z = z;
    }
}

/// <summary>Bezier in/out handle for a curved vertex. Straight OSM-derived geometry never needs these.</summary>
public sealed class InOutHandle
{
    [JsonPropertyName("point_idx")]
    public int PointIdx { get; set; }

    [JsonPropertyName("in_x")]
    public double InX { get; set; }

    [JsonPropertyName("in_z")]
    public double InZ { get; set; }

    [JsonPropertyName("out_x")]
    public double OutX { get; set; }

    [JsonPropertyName("out_z")]
    public double OutZ { get; set; }
}

/// <summary>One placeholder object per path vertex — structurally required, purely internal to the editor.</summary>
public sealed class PointObject
{
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = "res://Source/World/WorldObjects/wo_placeholder.tscn";

    [JsonPropertyName("position_x")]
    public double PositionX { get; set; }

    [JsonPropertyName("position_y")]
    public double PositionY { get; set; }

    [JsonPropertyName("position_z")]
    public double PositionZ { get; set; }

    [JsonPropertyName("object_type")]
    public int ObjectType { get; set; } = 1;

    [JsonPropertyName("assigned_path_point_idx")]
    public int AssignedPathPointIdx { get; set; }

    /// <summary>
    /// Per-vertex spline width override. Roads get their width from the stretched tile mesh and
    /// only set this sparsely as a local tweak, but rivers (paw_river_deep) set it on every single
    /// vertex — it's their only width control. Without it a river spline renders at zero width
    /// (invisible). Confirmed against template.mommap: every river vertex has this set, no other
    /// path type (roads aside) does.
    /// </summary>
    [JsonPropertyName("scale_custom_x")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ScaleCustomX { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>The actual visible mesh for a path segment — CoK does not render most path types
/// procedurally from the spline alone. Roads/walls/palisades/fences get one stretched tile prefab
/// per straight segment; rivers get an unskinned rectangle placeholder sized to the water width
/// instead (see <see cref="MapPath.LinesBorderPoints"/>, which rivers also require). Only true
/// procedural fills (forest/water/farmland *plots*) leave this empty.</summary>
public sealed class LineObjectGroup
{
    [JsonPropertyName("objects")]
    public List<MapObject> Objects { get; set; } = new();
}

/// <summary>Left/right riverbank offset-curve points for one segment — rivers only. Confirmed
/// present (non-empty) on every river segment in template.mommap; likely what actually drives the
/// water mesh/shader, alongside the placeholder rectangle in <see cref="LineObjectGroup"/>.</summary>
public sealed class LineBorderPointSet
{
    [JsonPropertyName("points_left")]
    public List<PathPoint> PointsLeft { get; set; } = new();

    [JsonPropertyName("points_right")]
    public List<PathPoint> PointsRight { get; set; } = new();
}

public sealed class ClickAndPlacingWo
{
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = "res://Source/World/WorldObjects/wo_placeholder_area.tscn";

    [JsonPropertyName("position_x")]
    public double PositionX { get; set; }

    [JsonPropertyName("position_y")]
    public double PositionY { get; set; }

    [JsonPropertyName("position_z")]
    public double PositionZ { get; set; }

    [JsonPropertyName("object_type")]
    public int ObjectType { get; set; } = 113;

    [JsonPropertyName("uid")]
    public int Uid { get; set; }

    [JsonPropertyName("assigned_path_area_idx")]
    public int AssignedPathAreaIdx { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// A path/spline/polygon feature: roads, rivers, hedges/fences, and the closed "Plots" (forest,
/// lake, farmland) whose interior fill CoK generates procedurally at load time — see plan notes.
/// </summary>
public sealed class MapPath
{
    [JsonPropertyName("filename")]
    public required string Filename { get; set; }

    [JsonPropertyName("position_x")]
    public double PositionX { get; set; }

    [JsonPropertyName("position_y")]
    public double PositionY { get; set; }

    [JsonPropertyName("position_z")]
    public double PositionZ { get; set; }

    [JsonPropertyName("path_type")]
    public required int PathType { get; set; }

    [JsonPropertyName("main_points")]
    public List<PathPoint> MainPoints { get; set; } = new();

    [JsonPropertyName("in_out_handles")]
    public List<InOutHandle> InOutHandles { get; set; } = new();

    [JsonPropertyName("points_objects")]
    public List<PointObject> PointsObjects { get; set; } = new();

    [JsonPropertyName("lines_objects")]
    public List<LineObjectGroup> LinesObjects { get; set; } = new();

    [JsonPropertyName("lines_custom_objects")]
    public List<JsonElement> LinesCustomObjects { get; set; } = new();

    [JsonPropertyName("lines_leaned_points")]
    public List<JsonElement> LinesLeanedPoints { get; set; } = new();

    [JsonPropertyName("lines_custom_scales")]
    public List<double> LinesCustomScales { get; set; } = new();

    [JsonPropertyName("click_and_placing_wo")]
    public required ClickAndPlacingWo ClickAndPlacingWo { get; set; }

    [JsonPropertyName("c_o_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? COType { get; set; }

    [JsonPropertyName("is_closed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsClosed { get; set; }

    [JsonPropertyName("height_changing_position_y")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? HeightChangingPositionY { get; set; }

    /// <summary>Riverbank offset curves, one entry per segment. Rivers only — see <see cref="LineBorderPointSet"/>.</summary>
    [JsonPropertyName("lines_border_points")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LineBorderPointSet>? LinesBorderPoints { get; set; }

    /// <summary>
    /// The actual interior fill content for a closed Plot (forest/farmland/etc): individual
    /// decoration/tree instances for forest, row objects for farmland. NOT generated procedurally
    /// at load time — CoK bakes this into the file when the shape is drawn/edited in the editor,
    /// so an empty list here (which is what a freshly-emitted forest polygon has by default,
    /// matching what the game itself writes for a plot with no fill yet) renders as an empty,
    /// invisible zone. Water plots (lake/ocean) always have this empty even when "full" — their
    /// fill is a shader/mesh effect, not discrete objects. Present (possibly empty) on every Plot
    /// path, absent on line-type paths (roads, rivers, barriers).
    /// </summary>
    [JsonPropertyName("area_objects")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<MapObject>? AreaObjects { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
