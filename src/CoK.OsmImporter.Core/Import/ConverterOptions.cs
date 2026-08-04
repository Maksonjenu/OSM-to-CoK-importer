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
}
