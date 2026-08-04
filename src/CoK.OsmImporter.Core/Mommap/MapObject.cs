using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoK.OsmImporter.Core.Mommap;

/// <summary>
/// A single point-placed mesh instance (tree, building, stone, decoration...). Prefabs carry a
/// lot of type-specific "custom_*" tuning fields (chimney offsets, grass tufts, on/off toggles)
/// that we never need to generate ourselves; anything we don't model explicitly round-trips
/// losslessly through <see cref="Extra"/> so loading and re-saving a real template never drops data.
/// </summary>
public sealed class MapObject
{
    [JsonPropertyName("filename")]
    public required string Filename { get; set; }

    [JsonPropertyName("position_x")]
    public double PositionX { get; set; }

    [JsonPropertyName("position_y")]
    public double PositionY { get; set; }

    [JsonPropertyName("position_z")]
    public double PositionZ { get; set; }

    [JsonPropertyName("rotation_y")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? RotationY { get; set; }

    [JsonPropertyName("scale_x")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ScaleX { get; set; }

    [JsonPropertyName("scale_z")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ScaleZ { get; set; }

    [JsonPropertyName("object_type")]
    public required int ObjectType { get; set; }

    [JsonPropertyName("height_changing_position_y")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? HeightChangingPositionY { get; set; }

    [JsonPropertyName("uid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Uid { get; set; }

    /// <summary>Set when this object is a stretched line-tile living inside a path's
    /// <c>lines_objects[i].objects</c> — which segment (0-based) it belongs to.</summary>
    [JsonPropertyName("assigned_path_line_idx")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AssignedPathLineIdx { get; set; }

    [JsonPropertyName("custom_data_g_size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? CustomDataGSize { get; set; }

    [JsonPropertyName("custom_size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? CustomSize { get; set; }

    /// <summary>Set false on river water-surface placeholder rectangles (see <see cref="Mommap.LineBorderPointSet"/>).</summary>
    [JsonPropertyName("is_clickable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsClickable { get; set; }

    /// <summary>Set on river water-surface placeholder rectangles (a fixed constant, 44, observed
    /// on every one in template.mommap) and on a Plot's baked area-fill objects (0 for a simple
    /// single-ring polygon) — distinct from the path's own <see cref="ClickAndPlacingWo.AssignedPathAreaIdx"/>.</summary>
    [JsonPropertyName("assigned_path_area_idx")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AssignedPathAreaIdx { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
