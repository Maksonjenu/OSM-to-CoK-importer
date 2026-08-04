using System.Text.Json;

namespace CoK.OsmImporter.Core.Mommap;

public static class MommapSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static MommapDocument Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<MommapDocument>(stream, Options)
               ?? throw new InvalidDataException($"'{path}' did not contain a valid .mommap document.");
    }

    public static void Save(MommapDocument document, string path)
    {
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, document, Options);
    }

    public static string SaveToString(MommapDocument document) =>
        JsonSerializer.Serialize(document, Options);
}
