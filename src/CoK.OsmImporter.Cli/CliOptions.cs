using System.Globalization;
using CoK.OsmImporter.Core.Geo;
using CoK.OsmImporter.Core.Import;

namespace CoK.OsmImporter.Cli;

internal sealed class CliArgumentException(string message) : Exception(message);

internal sealed class CliOptions
{
    public string? OsmPath { get; private set; }
    public string? OutPath { get; private set; }
    public string? TemplatePath { get; private set; }
    public string? MappingPath { get; private set; }
    public string? InitMappingPath { get; private set; }
    public string? UnmappedReportPath { get; private set; }
    public BoundingBox? Bbox { get; private set; }
    public double MetersPerUnit { get; private set; } = 1.0;
    public ImportProfile Profile { get; private set; } = ImportProfile.Full;
    public MergeMode Merge { get; private set; } = MergeMode.Append;
    public double SimplificationUnits { get; private set; } = 2.0;

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        GeoPoint? center = null;
        double? radius = null;
        BoundingBox? explicitBbox = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--osm":
                    options.OsmPath = RequireValue(args, ref i, "--osm");
                    break;
                case "--out":
                    options.OutPath = RequireValue(args, ref i, "--out");
                    break;
                case "--template":
                    options.TemplatePath = RequireValue(args, ref i, "--template");
                    break;
                case "--mapping":
                    options.MappingPath = RequireValue(args, ref i, "--mapping");
                    break;
                case "--init-mapping":
                    options.InitMappingPath = RequireValue(args, ref i, "--init-mapping");
                    break;
                case "--unmapped-report":
                    options.UnmappedReportPath = RequireValue(args, ref i, "--unmapped-report");
                    break;
                case "--scale":
                    options.MetersPerUnit = ParseDouble(RequireValue(args, ref i, "--scale"), "--scale");
                    if (options.MetersPerUnit <= 0)
                        throw new CliArgumentException("--scale must be positive.");
                    break;
                case "--profile":
                    options.Profile = ParseEnum<ImportProfile>(RequireValue(args, ref i, "--profile"), "--profile", "full", "base");
                    break;
                case "--merge":
                    options.Merge = ParseEnum<MergeMode>(RequireValue(args, ref i, "--merge"), "--merge", "append", "replace");
                    break;
                case "--bbox":
                    explicitBbox = ParseBbox(RequireValue(args, ref i, "--bbox"));
                    break;
                case "--center":
                    center = ParseCenter(RequireValue(args, ref i, "--center"));
                    break;
                case "--radius":
                    radius = ParseDouble(RequireValue(args, ref i, "--radius"), "--radius");
                    if (radius <= 0)
                        throw new CliArgumentException("--radius must be positive.");
                    break;
                case "--simplify":
                    options.SimplificationUnits = ParseDouble(RequireValue(args, ref i, "--simplify"), "--simplify");
                    if (options.SimplificationUnits < 0)
                        throw new CliArgumentException("--simplify must be zero or positive (0 disables simplification).");
                    break;
                default:
                    throw new CliArgumentException($"Unrecognized argument '{args[i]}'.");
            }
        }

        if (options.InitMappingPath is not null)
            return options; // nothing else is required for this mode

        if (string.IsNullOrEmpty(options.OsmPath))
            throw new CliArgumentException("--osm is required.");
        if (string.IsNullOrEmpty(options.OutPath))
            throw new CliArgumentException("--out is required.");
        if (!File.Exists(options.OsmPath))
            throw new CliArgumentException($"--osm file not found: '{options.OsmPath}'.");
        if (options.TemplatePath is not null && !File.Exists(options.TemplatePath))
            throw new CliArgumentException($"--template file not found: '{options.TemplatePath}'.");
        if (options.MappingPath is not null && !File.Exists(options.MappingPath))
            throw new CliArgumentException($"--mapping file not found: '{options.MappingPath}'.");

        if (explicitBbox is not null && center is not null)
            throw new CliArgumentException("Specify either --bbox or --center/--radius, not both.");
        if (center is not null != radius is not null)
            throw new CliArgumentException("--center and --radius must be given together.");

        options.Bbox = explicitBbox ?? (center is not null ? BoundingBox.FromCenterRadius(center.Value, radius!.Value) : null);

        return options;
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new CliArgumentException($"{flag} requires a value.");
        return args[++i];
    }

    private static double ParseDouble(string s, string flag)
    {
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new CliArgumentException($"{flag}: '{s}' is not a valid number.");
        return value;
    }

    private static T ParseEnum<T>(string s, string flag, string a, string b) where T : struct, Enum
    {
        if (Enum.TryParse<T>(s, ignoreCase: true, out var value) &&
            (string.Equals(s, a, StringComparison.OrdinalIgnoreCase) || string.Equals(s, b, StringComparison.OrdinalIgnoreCase)))
            return value;
        throw new CliArgumentException($"{flag} must be '{a}' or '{b}', got '{s}'.");
    }

    private static BoundingBox ParseBbox(string s)
    {
        var parts = s.Split(',');
        if (parts.Length != 4)
            throw new CliArgumentException("--bbox must be 'minLat,minLon,maxLat,maxLon'.");

        var values = parts.Select(p => ParseDouble(p.Trim(), "--bbox")).ToArray();
        var (minLat, minLon, maxLat, maxLon) = (values[0], values[1], values[2], values[3]);
        if (minLat >= maxLat || minLon >= maxLon)
            throw new CliArgumentException("--bbox: minLat/minLon must be less than maxLat/maxLon.");

        return new BoundingBox(minLat, minLon, maxLat, maxLon);
    }

    private static GeoPoint ParseCenter(string s)
    {
        var parts = s.Split(',');
        if (parts.Length != 2)
            throw new CliArgumentException("--center must be 'lat,lon'.");

        return new GeoPoint(ParseDouble(parts[0].Trim(), "--center"), ParseDouble(parts[1].Trim(), "--center"));
    }
}
