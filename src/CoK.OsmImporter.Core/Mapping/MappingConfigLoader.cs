using System.Reflection;
using System.Text.Json;

namespace CoK.OsmImporter.Core.Mapping;

public static class MappingConfigLoader
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string LoadDefaultRawJson()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "CoK.OsmImporter.Core.Mapping.default-mapping.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static MappingConfig LoadDefault() =>
        JsonSerializer.Deserialize<MappingConfig>(LoadDefaultRawJson(), Options)
        ?? throw new InvalidDataException("Embedded default-mapping.json failed to deserialize.");

    public static MappingConfig Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<MappingConfig>(stream, Options)
               ?? throw new InvalidDataException($"'{path}' did not contain a valid mapping config.");
    }

    /// <summary>Writes the built-in default mapping config to disk so the user can edit it (--init-mapping).</summary>
    public static void WriteDefaultTo(string path) => File.WriteAllText(path, LoadDefaultRawJson());
}
