using CoK.OsmImporter.Core.AssetCatalog;
using CoK.OsmImporter.Core.Geo;
using CoK.OsmImporter.Core.Import;
using CoK.OsmImporter.Core.Mapping;
using CoK.OsmImporter.Core.Mommap;
using CoK.OsmImporter.Core.Osm;

namespace CoK.OsmImporter.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (CliArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine();
            PrintHelp();
            return 1;
        }

        if (options.InitMappingPath is { } initPath)
        {
            MappingConfigLoader.WriteDefaultTo(initPath);
            Console.WriteLine($"Wrote default mapping config to '{initPath}'.");
            return 0;
        }

        try
        {
            return Run(options);
        }
        catch (AssetNotFoundException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or FormatException or InvalidOperationException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Run(CliOptions options)
    {
        Console.WriteLine($"Parsing OSM file '{options.OsmPath}'...");
        var osm = OsmXmlParser.Parse(options.OsmPath!);
        Console.WriteLine($"  {osm.Nodes.Count} nodes, {osm.Ways.Count} ways, {osm.Relations.Count} relations.");

        var mapping = options.MappingPath is { } mappingPath
            ? MappingConfigLoader.Load(mappingPath)
            : MappingConfigLoader.LoadDefault();
        var catalog = AssetCatalog.LoadEmbedded();

        var target = options.TemplatePath is { } templatePath
            ? MommapSerializer.Load(templatePath)
            : MommapDocument.CreateBlank();

        if (options.TemplatePath is not null)
            Console.WriteLine($"Loaded base template '{options.TemplatePath}' " +
                               $"({target.Objects.Count} objects, {target.Paths.Count} paths).");

        var converterOptions = new ConverterOptions
        {
            Bbox = options.Bbox,
            MetersPerUnit = options.MetersPerUnit,
            Profile = options.Profile,
            Merge = options.Merge,
            SimplificationUnits = options.SimplificationUnits,
            MaxFillObjectsPerPolygon = options.MaxFillObjectsPerPolygon,
            MaxPlotAreaUnits = options.MaxPlotAreaUnits,
        };

        if (options.Bbox is null)
            Console.WriteLine("No --bbox/--center given: importing the whole file's extent. " +
                               "This can produce a very large map — consider --bbox/--center+--radius " +
                               "to cut it down to the area you actually want.");

        var converter = new OsmToMommapConverter(mapping, catalog);
        var summary = converter.Convert(osm, target, converterOptions);

        MommapSerializer.Save(target, options.OutPath!);
        Console.WriteLine($"Wrote '{options.OutPath}' ({target.Objects.Count} objects, {target.Paths.Count} paths).");
        Console.WriteLine();
        Console.WriteLine(summary.ToString());

        if (options.UnmappedReportPath is { } reportPath)
        {
            summary.WriteUnmappedReportCsv(reportPath);
            Console.WriteLine();
            Console.WriteLine($"Unmapped-tags report written to '{reportPath}'.");
        }

        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            CoK.OsmImporter.Cli — imports OpenStreetMap data into a Canvas of Kings .mommap map.

            Usage:
              cok-osm-import --osm <file.osm> --out <file.mommap> [options]

            Required:
              --osm <path>                Source OSM XML export.
              --out <path>                Output .mommap path.

            Area selection (optional — omit both for the whole file's extent):
              --bbox <minLat,minLon,maxLat,maxLon>
              --center <lat,lon> --radius <meters>

            Options:
              --template <path.mommap>   Base map to build on (keeps its environment/settings/
                                          existing content). Omit for a blank canvas.
              --profile <full|base>      full = roads+water+forest/farmland+trees+buildings (default).
                                          base = same minus buildings.
              --scale <metersPerUnit>    Real-world meters per CoK map unit. Default 1.0.
              --simplify <units>         Douglas-Peucker line-simplification tolerance, in output
                                          map units (NOT scaled by --scale). OSM ways are densely
                                          GPS-sampled — CoK's path tools reject/mis-render the raw
                                          point density and sharp micro-zigzags ("path invalid").
                                          Default 2.0; 0 disables it; raise it if you still see
                                          "path invalid" errors.
              --max-fill <count>         Cap on baked interior-fill objects (trees, grass...) per
                                          forest/farmland plot. CoK bakes this fill into the file
                                          itself (not generated at load time) and its own density is
                                          tuned for small hand-drawn zones — an uncapped real OSM
                                          forest polygon can be hundreds of hectares, producing
                                          millions of objects and a multi-hundred-MB file. Default
                                          2000; 0 disables area fill entirely (empty, invisible
                                          zones, same as pre-fill behavior).
              --max-plot-area <units²>   Split a Plot polygon (forest/farmland/water) larger than
                                          this into a grid of smaller pieces instead of emitting it
                                          whole. CoK appears to glitch/warn ("area too large") on an
                                          oversized plot — the exact threshold isn't known, this
                                          defaults to the largest confirmed-working reference size
                                          (2000). Splitting leaves a visible seam along grid lines.
                                          0 or negative disables splitting.
              --merge <append|replace>   append (default) adds to --template's existing content;
                                          replace wipes its objects/paths first.
              --mapping <path>           Use a custom mapping config instead of the built-in default.
              --init-mapping <path>      Write the built-in default mapping config to <path> and exit
                                          (edit it, then pass it back via --mapping).
              --unmapped-report <path>   Write a CSV of tag combinations that had no mapping rule.
              --help                     Show this help.
            """);
    }
}
