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

    [Fact]
    public void SplitPolygonIntoGrid_SmallPolygon_ReturnsUnchanged()
    {
        var ring = new List<LocalPoint> { new(0, 0), new(10, 0), new(10, 10), new(0, 10) }; // area 100

        var pieces = GeometryHelpers.SplitPolygonIntoGrid(ring, maxArea: 500);

        var piece = Assert.Single(pieces);
        Assert.Equal(ring, piece);
    }

    [Fact]
    public void SplitPolygonIntoGrid_DisabledWhenMaxAreaNonPositive()
    {
        var ring = new List<LocalPoint> { new(0, 0), new(100, 0), new(100, 100), new(0, 100) };

        var pieces = GeometryHelpers.SplitPolygonIntoGrid(ring, maxArea: 0);

        Assert.Single(pieces);
    }

    /// <summary>
    /// Regression test for a real bug: real OSM forest/farmland polygons can be far larger than
    /// anything hand-drawn in CoK, and CoK appears to reject/glitch on an oversized plot
    /// ("area too large"). A big polygon must come back as multiple pieces, each within the cap,
    /// and together covering the same total area as the original (splitting must not lose land).
    /// </summary>
    [Fact]
    public void SplitPolygonIntoGrid_LargePolygon_SplitsIntoPiecesWithinCapAndPreservesTotalArea()
    {
        var ring = new List<LocalPoint> { new(0, 0), new(100, 0), new(100, 100), new(0, 100) }; // area 10000
        const double maxArea = 900.0;

        var pieces = GeometryHelpers.SplitPolygonIntoGrid(ring, maxArea);

        Assert.True(pieces.Count > 1, "a 10000-unit² polygon capped at 900 should split into more than one piece.");
        var totalArea = pieces.Sum(p => Math.Abs(GeometryHelpers.SignedArea(p)));
        Assert.Equal(10000.0, totalArea, precision: 3);

        // Each piece is clipped to fit inside one cellSize x cellSize grid cell (cellSize =
        // sqrt(maxArea)), so no piece's area can exceed maxArea itself (tiny fp slack aside).
        Assert.All(pieces, p => Assert.True(Math.Abs(GeometryHelpers.SignedArea(p)) <= maxArea * 1.001));
    }

    [Fact]
    public void SplitPolygonIntoGrid_WorksOnNonConvexPolygon()
    {
        // An "L" shape (non-convex), area = 100*100 - 50*50 = 7500.
        var ring = new List<LocalPoint>
        {
            new(0, 0), new(100, 0), new(100, 50), new(50, 50), new(50, 100), new(0, 100),
        };
        const double maxArea = 1000.0;

        var pieces = GeometryHelpers.SplitPolygonIntoGrid(ring, maxArea);

        Assert.True(pieces.Count > 1);
        var totalArea = pieces.Sum(p => Math.Abs(GeometryHelpers.SignedArea(p)));
        Assert.Equal(7500.0, totalArea, precision: 3);
    }

    [Fact]
    public void BuildBufferPolygon_StraightTwoPointLine_ProducesExactRectangle()
    {
        var centerline = new List<LocalPoint> { new(0, 0), new(10, 0) };

        var ring = GeometryHelpers.BuildBufferPolygon(centerline, width: 4.0);

        Assert.Equal(4, ring.Count);
        Assert.Contains(ring, p => Approximately(p, 0, 2));
        Assert.Contains(ring, p => Approximately(p, 10, 2));
        Assert.Contains(ring, p => Approximately(p, 10, -2));
        Assert.Contains(ring, p => Approximately(p, 0, -2));
    }

    [Fact]
    public void BuildBufferPolygon_EveryVertexIsHalfWidthFromCenterline()
    {
        var centerline = new List<LocalPoint> { new(0, 0), new(5, 5), new(15, 5) };
        const double width = 6.0;

        var ring = GeometryHelpers.BuildBufferPolygon(centerline, width);

        // Every ring point must be at least half the width away from the nearest centerline
        // vertex (a mitered offset at a bend can be slightly further, never closer).
        foreach (var ringPoint in ring)
        {
            var minDist = centerline.Min(c => Math.Sqrt(Math.Pow(ringPoint.X - c.X, 2) + Math.Pow(ringPoint.Z - c.Z, 2)));
            Assert.True(minDist >= width / 2.0 - 1e-6, $"ring point {ringPoint} was only {minDist} from the centerline (expected >= {width / 2.0})");
        }
    }

    [Fact]
    public void BuildBufferPolygon_PointCountIsDoubleTheCenterline()
    {
        var centerline = Enumerable.Range(0, 7).Select(i => new LocalPoint(i, 0)).ToList();

        var ring = GeometryHelpers.BuildBufferPolygon(centerline, width: 2.0);

        Assert.Equal(centerline.Count * 2, ring.Count);
    }

    private static bool Approximately(LocalPoint p, double x, double z, double tolerance = 1e-6) =>
        Math.Abs(p.X - x) < tolerance && Math.Abs(p.Z - z) < tolerance;
}
