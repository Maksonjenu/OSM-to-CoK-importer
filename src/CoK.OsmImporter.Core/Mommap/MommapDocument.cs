using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoK.OsmImporter.Core.Mommap;

public sealed class MommapInfo
{
    [JsonPropertyName("application_name")]
    public string ApplicationName { get; set; } = "Canvas of Kings";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "0.1.0.42";

    [JsonPropertyName("camera_pos_x")]
    public double CameraPosX { get; set; }

    [JsonPropertyName("camera_pos_z")]
    public double CameraPosZ { get; set; }

    [JsonPropertyName("camera_zoom")]
    public double CameraZoom { get; set; } = 16.0;

    [JsonPropertyName("camera_rotation")]
    public double CameraRotation { get; set; }

    [JsonPropertyName("steam_file_id")]
    public int SteamFileId { get; set; } = -1;

    [JsonPropertyName("map_layers_open")]
    public bool MapLayersOpen { get; set; }

    /// <summary>Highest uid used anywhere in the document. New paths/objects that need a uid
    /// (currently just each path's <see cref="ClickAndPlacingWo"/>) consume the next value.</summary>
    [JsonPropertyName("last_uid")]
    public int LastUid { get; set; } = -1;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// Root of a .mommap save file. <see cref="Objects"/> and <see cref="Paths"/> are the two arrays
/// we generate content into; everything else (environment lighting, paper/grid settings, deco
/// layers) is carried through opaquely from whatever base template was loaded, since we have no
/// reason to interpret or change it.
/// </summary>
public sealed class MommapDocument
{
    [JsonPropertyName("info")]
    public MommapInfo Info { get; set; } = new();

    [JsonPropertyName("layers")]
    public JsonElement Layers { get; set; } = DefaultLayers;

    [JsonPropertyName("objects")]
    public List<MapObject> Objects { get; set; } = new();

    [JsonPropertyName("paths")]
    public List<MapPath> Paths { get; set; } = new();

    [JsonPropertyName("environment")]
    public JsonElement Environment { get; set; } = DefaultCanvas.Environment;

    [JsonPropertyName("map_settings")]
    public JsonElement MapSettings { get; set; } = DefaultCanvas.MapSettings;

    [JsonPropertyName("deco_tool")]
    public JsonElement DecoTool { get; set; } = DefaultCanvas.DecoTool;

    [JsonPropertyName("deco_images")]
    public JsonElement DecoImages { get; set; } = EmptyArray;

    [JsonPropertyName("deco_labels")]
    public JsonElement DecoLabels { get; set; } = EmptyArray;

    [JsonPropertyName("deco_legends")]
    public JsonElement DecoLegends { get; set; } = EmptyArray;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    private static readonly JsonElement EmptyArray = JsonDocument.Parse("[]").RootElement;
    private static readonly JsonElement DefaultLayers =
        JsonDocument.Parse("""{"last_idx":-1,"map_layers":[]}""").RootElement;

    /// <summary>A brand-new, empty canvas (used when the CLI is run without --template).</summary>
    public static MommapDocument CreateBlank() => new();
}

/// <summary>
/// Real environment/map_settings/deco_tool values for a fresh canvas, lifted verbatim from
/// template.mommap. An empty `{}` here is NOT a safe default — CoK renders it as a gray, unlit
/// screen with fully transparent lighting/fog/water colors instead of gracefully falling back to
/// its own defaults, so <see cref="MommapDocument.CreateBlank"/> must ship real values.
/// </summary>
internal static class DefaultCanvas
{
    // Deliberately never disposed: RootElement subtrees below stay valid for the process lifetime.
    private static readonly JsonDocument Document = Load();

    public static readonly JsonElement Environment = Document.RootElement.GetProperty("environment");
    public static readonly JsonElement MapSettings = Document.RootElement.GetProperty("map_settings");
    public static readonly JsonElement DecoTool = Document.RootElement.GetProperty("deco_tool");

    private static JsonDocument Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "CoK.OsmImporter.Core.Mommap.default-canvas.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        return JsonDocument.Parse(stream);
    }
}
