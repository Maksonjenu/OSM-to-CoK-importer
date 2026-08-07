using System.Globalization;
using CoK.OsmImporter.Core.Geo;

namespace CoK.OsmImporter.Core.Reporting;

public sealed record UnmappedFeature(string ElementType, long Id, string TagsSummary, string Reason);

public sealed class ImportSummary
{
    public int Roads;
    public int Bridges;
    public int Rivers;
    public int WaterPolygons;
    public int ForestPolygons;
    public int FarmlandPolygons;
    public int Barriers;
    public int Trees;
    public int Buildings;
    public int BuildingsSkippedByProfile;
    public int SkippedComplexRelations;

    public BoundingBox UsedBbox { get; set; }
    public List<UnmappedFeature> Unmapped { get; } = new();

    public override string ToString()
    {
        var ci = CultureInfo.InvariantCulture;
        var lines = new List<string>
        {
            string.Create(ci, $"bbox: lat[{UsedBbox.MinLat:F6},{UsedBbox.MaxLat:F6}] lon[{UsedBbox.MinLon:F6},{UsedBbox.MaxLon:F6}] " +
            $"(~{UsedBbox.WidthMeters:F0}m x {UsedBbox.HeightMeters:F0}m)"),
            $"roads: {Roads}" + (Bridges > 0 ? $" ({Bridges} bridges)" : ""),
            $"rivers/streams: {Rivers}",
            $"water polygons: {WaterPolygons}",
            $"forest/scrub polygons: {ForestPolygons}",
            $"farmland polygons: {FarmlandPolygons}",
            $"barriers (fences/walls/hedges): {Barriers}",
            $"trees: {Trees}",
            $"buildings: {Buildings}" + (BuildingsSkippedByProfile > 0 ? $" ({BuildingsSkippedByProfile} skipped, --profile base)" : ""),
            $"skipped multipolygon relations (too complex for v1): {SkippedComplexRelations}",
            $"unmapped tag combinations: {Unmapped.Count}",
        };
        return string.Join(Environment.NewLine, lines);
    }

    public void WriteUnmappedReportCsv(string path)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("element_type,id,tags,reason");
        foreach (var u in Unmapped)
            writer.WriteLine($"{Csv(u.ElementType)},{u.Id},{Csv(u.TagsSummary)},{Csv(u.Reason)}");

        static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
