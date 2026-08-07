using CoK.OsmImporter.Core.Geo;

namespace CoK.OsmImporter.Core.Import;

public enum ImportProfile
{
    /// <summary>Roads, water, forest/farmland, trees, and buildings.</summary>
    Full,

    /// <summary>Everything in <see cref="Full"/> except buildings — "just roads and landscape".</summary>
    Base,
}

public enum MergeMode
{
    /// <summary>Add imported content to whatever the base template already contains.</summary>
    Append,

    /// <summary>Replace the base template's objects/paths entirely with the imported content.</summary>
    Replace,
}

public sealed class ConverterOptions
{
    /// <summary>Area to import. Null means "the whole OSM file's &lt;bounds&gt; (or computed node extent)".</summary>
    public BoundingBox? Bbox { get; init; }

    /// <summary>Real-world meters represented by one CoK map unit. Default 1:1.</summary>
    public double MetersPerUnit { get; init; } = 1.0;

    public ImportProfile Profile { get; init; } = ImportProfile.Full;

    public MergeMode Merge { get; init; } = MergeMode.Append;

    /// <summary>
    /// Douglas-Peucker simplification tolerance, in output map units — deliberately NOT scaled by
    /// <see cref="MetersPerUnit"/>, unlike width/coordinates. The problem this solves (CoK's path
    /// tools rejecting/mis-rendering dense, sharply-zigzagging geometry with "path invalid") is a
    /// property of the final on-disk point count/angles, not of real-world accuracy — at a high
    /// --scale a real-meters tolerance would shrink to nearly nothing and stop helping. 0 disables
    /// simplification.
    /// </summary>
    public double SimplificationUnits { get; init; } = 2.0;

    /// <summary>
    /// Hard cap on baked area-fill objects (see MapPath.AreaObjects) per polygon. Real-world
    /// natural=wood/landuse=forest OSM polygons can cover hundreds of hectares — CoK's own
    /// ~1 object/unit² fill density was clearly tuned for small hand-drawn zones (observed max:
    /// ~2000 objects for the largest reference test zone), and applying it uncoupled to a
    /// real-world forest's actual area produces file sizes and object counts in the hundreds of
    /// thousands to millions, which is impractical for both the .mommap file and CoK itself to
    /// load. Default matches that observed real-world reference ceiling.
    /// </summary>
    public int MaxFillObjectsPerPolygon { get; init; } = 2000;

    /// <summary>
    /// Hard cap on a single Plot polygon's area, in output map units² — larger polygons get split
    /// into a grid of smaller pieces (see GeometryHelpers.SplitPolygonIntoGrid) rather than emitted
    /// whole. Reported in-game as an "area too large" glitch/warning on real OSM-derived forest
    /// polygons (which can cover hundreds of hectares — far beyond anything hand-drawn in CoK).
    /// The exact CoK-side threshold isn't known; this defaults to the largest area confirmed
    /// working in a real hand-drawn reference file (~2095 units²), same reasoning as
    /// MaxFillObjectsPerPolygon. Splitting trades a visible seam along the grid lines for keeping
    /// every piece within whatever CoK's real limit is. 0 or negative disables splitting.
    /// </summary>
    public double MaxPlotAreaUnits { get; init; } = 2000.0;

    /// <summary>
    /// EXPERIMENTAL: draw waterways (rivers/streams) as a CoK water *plot* (same procedural fill
    /// as lakes — <see cref="MappingConfig.WaterPolygon"/>'s asset) instead of the river *path*
    /// spline type. The river path format is a best-effort reconstruction that still produces
    /// "path invalid" on real OSM data; plots have proven robust against complex, many-point OSM
    /// shapes (lake imports work fine), so this sidesteps the problem by not using the spline path
    /// at all. Default true on this branch — set false to fall back to the spline-based renderer.
    /// </summary>
    public bool RiversAsWaterPolygons { get; init; } = true;

    /// <summary>
    /// EXPERIMENTAL: size of one elevation-grid cell, in output map units (NOT scaled by --scale —
    /// same "aesthetic/output-facing, not real-world" reasoning as SimplificationUnits) — the bbox
    /// is tiled into square cells this size, each becoming its own independent
    /// papl_plateau_plateau.tscn Plot raised to that cell's sampled elevation (CoK has no smooth
    /// heightmap; a Plateau plot is a flat raised area with its own footprint — see TODO.md). Null
    /// (the default) disables elevation entirely — it requires a network call per import
    /// (OpenMeteoElevationProvider) and is opt-in via the CLI's --elevation flag.
    /// </summary>
    public double? ElevationGridUnits { get; init; }

    /// <summary>
    /// Real-world meters of elevation represented by one CoK plateau-height unit. A Plateau's
    /// height_changing_position_y caps out around 30 (see MappingConfig notes/TODO.md) while real
    /// terrain can vary by tens to hundreds of meters within a single map — this needs its own
    /// independent scale from the horizontal --scale, same reasoning as why SimplificationUnits/
    /// RowSpacing aren't tied to --scale either, just inverted (this one IS about a real-world
    /// quantity, just a different one than horizontal distance). Only meaningful when
    /// <see cref="ElevationGridUnits"/> is set.
    /// </summary>
    public double ElevationScale { get; init; } = 5.0;
}
