using CoK.OsmImporter.Core.Geo;
using CoK.OsmImporter.Core.Import;

namespace CoK.OsmImporter.Core.Tests;

public class GeometryHelpersTests
{
    [Fact]
    public void SimplifyPolyline_RemovesPointsWithinTolerance()
    {
        // A near-straight line with one point that's only slightly off the direct path.
        var points = new List<LocalPoint>
        {
            new(0, 0),
            new(5, 0.1), // within a 1.0 tolerance of the 0,0 -> 10,0 line
            new(10, 0),
        };

        var simplified = GeometryHelpers.SimplifyPolyline(points, epsilon: 1.0);

        Assert.Equal(2, simplified.Count);
        Assert.Equal(points[0], simplified[0]);
        Assert.Equal(points[^1], simplified[^1]);
    }

    [Fact]
    public void SimplifyPolyline_KeepsPointsBeyondTolerance()
    {
        var points = new List<LocalPoint>
        {
            new(0, 0),
            new(5, 5), // well outside a 1.0 tolerance of the 0,0 -> 10,0 line
            new(10, 0),
        };

        var simplified = GeometryHelpers.SimplifyPolyline(points, epsilon: 1.0);

        Assert.Equal(3, simplified.Count);
    }

    [Fact]
    public void SimplifyPolyline_AlwaysKeepsFirstAndLastPoint()
    {
        var points = Enumerable.Range(0, 50).Select(i => new LocalPoint(i * 0.1, 0)).ToList();

        var simplified = GeometryHelpers.SimplifyPolyline(points, epsilon: 5.0);

        Assert.Equal(points[0], simplified[0]);
        Assert.Equal(points[^1], simplified[^1]);
    }

    [Fact]
    public void SimplifyPolyline_ZeroEpsilon_IsANoOp()
    {
        var points = new List<LocalPoint> { new(0, 0), new(5, 0.001), new(10, 0) };

        var simplified = GeometryHelpers.SimplifyPolyline(points, epsilon: 0);

        Assert.Equal(points.Count, simplified.Count);
    }

    [Fact]
    public void SimplifyPolyline_ShortInput_ReturnsUnchanged()
    {
        var points = new List<LocalPoint> { new(0, 0), new(10, 0) };

        var simplified = GeometryHelpers.SimplifyPolyline(points, epsilon: 1.0);

        Assert.Equal(2, simplified.Count);
    }
}
