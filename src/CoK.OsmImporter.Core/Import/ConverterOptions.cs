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
}
