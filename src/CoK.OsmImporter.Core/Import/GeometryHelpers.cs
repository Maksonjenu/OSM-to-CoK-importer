using CoK.OsmImporter.Core.Geo;

namespace CoK.OsmImporter.Core.Import;

internal static class GeometryHelpers
{
    /// <summary>Drops the duplicated closing point of a ring (first == last), if present.</summary>
    public static IReadOnlyList<LocalPoint> DedupeClosingPoint(IReadOnlyList<LocalPoint> points)
    {
        if (points.Count > 1 && points[0].Equals(points[^1]))
            return points.Take(points.Count - 1).ToList();
        return points;
    }

    public static LocalPoint Centroid(IReadOnlyList<LocalPoint> points) =>
        new(points.Average(p => p.X), points.Average(p => p.Z));

    /// <summary>
    /// Rough placement heading for a building footprint: the direction of its longest boundary
    /// edge, in degrees. Not a rigorous "front door faces the street" computation — just gives
    /// buildings some non-uniform, footprint-informed rotation instead of all facing the same way.
    /// </summary>
    public static double DominantEdgeAngleDegrees(IReadOnlyList<LocalPoint> ring)
    {
        if (ring.Count < 2)
            return 0.0;

        var bestLenSq = -1.0;
        var bestAngle = 0.0;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            var dx = b.X - a.X;
            var dz = b.Z - a.Z;
            var lenSq = dx * dx + dz * dz;
            if (lenSq > bestLenSq)
            {
                bestLenSq = lenSq;
                bestAngle = Math.Atan2(dx, dz) * 180.0 / Math.PI;
            }
        }

        return bestAngle;
    }

    /// <summary>
    /// Douglas-Peucker polyline simplification. OSM ways are densely, unevenly sampled GPS
    /// surveys — far more points than a hand-drawn CoK path would ever have, including
    /// near-duplicate/zero-length segments and sharp micro-zigzags. CoK's path tools appear to
    /// reject or mis-render geometry like that ("path invalid", degenerate segments) — this
    /// thins the point count down to something a human could plausibly have drawn while keeping
    /// the overall shape within <paramref name="epsilon"/> map units.
    /// </summary>
    public static List<LocalPoint> SimplifyPolyline(IReadOnlyList<LocalPoint> points, double epsilon)
    {
        if (points.Count < 3 || epsilon <= 0)
            return points.ToList();

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        SimplifySection(points, 0, points.Count - 1, epsilon, keep);

        var result = new List<LocalPoint>(points.Count);
        for (var i = 0; i < points.Count; i++)
            if (keep[i])
                result.Add(points[i]);
        return result;
    }

    private static void SimplifySection(IReadOnlyList<LocalPoint> points, int startIdx, int endIdx, double epsilon, bool[] keep)
    {
        if (endIdx <= startIdx + 1)
            return;

        var start = points[startIdx];
        var end = points[endIdx];
        var maxDist = -1.0;
        var maxIdx = -1;
        for (var i = startIdx + 1; i < endIdx; i++)
        {
            var d = PerpendicularDistance(points[i], start, end);
            if (d > maxDist)
            {
                maxDist = d;
                maxIdx = i;
            }
        }

        if (maxDist > epsilon)
        {
            keep[maxIdx] = true;
            SimplifySection(points, startIdx, maxIdx, epsilon, keep);
            SimplifySection(points, maxIdx, endIdx, epsilon, keep);
        }
    }

    private static double PerpendicularDistance(LocalPoint p, LocalPoint a, LocalPoint b)
    {
        var dx = b.X - a.X;
        var dz = b.Z - a.Z;
        var lenSq = dx * dx + dz * dz;
        if (lenSq < 1e-12)
            return Math.Sqrt(Math.Pow(p.X - a.X, 2) + Math.Pow(p.Z - a.Z, 2));

        var t = ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / lenSq;
        var projX = a.X + t * dx;
        var projZ = a.Z + t * dz;
        return Math.Sqrt(Math.Pow(p.X - projX, 2) + Math.Pow(p.Z - projZ, 2));
    }

    public static double SignedArea(IReadOnlyList<LocalPoint> ring)
    {
        var a = 0.0;
        var n = ring.Count;
        for (var i = 0; i < n; i++)
        {
            var p1 = ring[i];
            var p2 = ring[(i + 1) % n];
            a += p1.X * p2.Z - p2.X * p1.Z;
        }
        return a / 2.0;
    }

    /// <summary>
    /// Scatters points uniformly at random inside a (possibly non-convex) polygon via rejection
    /// sampling, at roughly <paramref name="density"/> points per unit² of the polygon's actual
    /// area. Used to bake CoK's forest-plot interior fill (see <see cref="MapObject"/>-building
    /// code in the converter) — CoK does this itself when a forest zone is drawn interactively in
    /// the editor and saves the result into the file; it does NOT regenerate it at load time, so an
    /// importer-created plot needs to bake its own fill the same way or it renders as an empty,
    /// invisible zone (confirmed against real hand-drawn CoK test data: ~1 object/unit², a
    /// forest-shaped ring with zero fill objects was the one case that stayed empty/never got
    /// filled in-editor either).
    /// </summary>
    public static List<LocalPoint> ScatterPointsInPolygon(IReadOnlyList<LocalPoint> ring, double density, Random rng, int maxCount = int.MaxValue)
    {
        var result = new List<LocalPoint>();
        if (ring.Count < 3 || density <= 0)
            return result;

        var area = Math.Abs(SignedArea(ring));
        var targetCount = Math.Min((int)Math.Round(area * density), maxCount);
        if (targetCount <= 0)
            return result;

        var minX = ring.Min(p => p.X);
        var maxX = ring.Max(p => p.X);
        var minZ = ring.Min(p => p.Z);
        var maxZ = ring.Max(p => p.Z);

        // Rejection sampling against the bounding box; capped so a thin/degenerate polygon (tiny
        // fill ratio inside its own bbox) can't spin forever — it'll just under-fill instead.
        var maxAttempts = Math.Max(targetCount * 50, 2000);
        var attempts = 0;
        while (result.Count < targetCount && attempts < maxAttempts)
        {
            attempts++;
            var x = minX + rng.NextDouble() * (maxX - minX);
            var z = minZ + rng.NextDouble() * (maxZ - minZ);
            if (IsPointInPolygon(ring, x, z))
                result.Add(new LocalPoint(x, z));
        }

        return result;
    }

    /// <summary>Standard ray-casting point-in-polygon test.</summary>
    private static bool IsPointInPolygon(IReadOnlyList<LocalPoint> ring, double x, double z)
    {
        var inside = false;
        var n = ring.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var pi = ring[i];
            var pj = ring[j];
            var crosses = pi.Z > z != pj.Z > z;
            if (!crosses)
                continue;

            var xIntersect = pj.X + (z - pj.Z) / (pi.Z - pj.Z) * (pi.X - pj.X);
            if (x < xIntersect)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// Splits a polygon into pieces no larger than <paramref name="maxArea"/> map units² by
    /// clipping it against a grid of square cells. Real-world OSM `natural=wood`/`landuse=forest`
    /// polygons can cover hundreds of hectares — CoK appears to reject or visually glitch on a
    /// plot beyond some (undocumented) area, matching the "path invalid" pattern already seen for
    /// dense line geometry. Splitting preserves full coverage (unlike just refusing to emit an
    /// oversized polygon) at the cost of a visible seam between pieces along the grid lines.
    /// Returns the original ring unchanged (as the only element) if it's already small enough or
    /// <paramref name="maxArea"/> is non-positive (splitting disabled).
    /// </summary>
    public static List<List<LocalPoint>> SplitPolygonIntoGrid(IReadOnlyList<LocalPoint> ring, double maxArea)
    {
        if (ring.Count < 3 || maxArea <= 0 || Math.Abs(SignedArea(ring)) <= maxArea)
            return new List<List<LocalPoint>> { ring.ToList() };

        var minX = ring.Min(p => p.X);
        var maxX = ring.Max(p => p.X);
        var minZ = ring.Min(p => p.Z);
        var maxZ = ring.Max(p => p.Z);
        var cellSize = Math.Max(Math.Sqrt(maxArea), 1e-3);

        var pieces = new List<List<LocalPoint>>();
        for (var x = minX; x < maxX; x += cellSize)
        {
            for (var z = minZ; z < maxZ; z += cellSize)
            {
                var clipped = ClipPolygonToRect(ring, x, z, x + cellSize, z + cellSize);
                if (clipped.Count >= 3 && Math.Abs(SignedArea(clipped)) > 0.5)
                    pieces.Add(clipped);
            }
        }

        // A degenerate/very thin polygon might clip to nothing usable — fall back to the
        // original rather than silently dropping the feature.
        return pieces.Count > 0 ? pieces : new List<List<LocalPoint>> { ring.ToList() };
    }

    /// <summary>
    /// Sutherland-Hodgman polygon clipping against an axis-aligned rectangle. Correct for any
    /// simple (possibly non-convex) subject polygon, since only the clip window needs to be convex
    /// — a rectangle always is.
    /// </summary>
    private static List<LocalPoint> ClipPolygonToRect(IReadOnlyList<LocalPoint> subject, double minX, double minZ, double maxX, double maxZ)
    {
        var points = subject.ToList();
        points = ClipEdge(points, p => p.X >= minX, (a, b) => IntersectX(a, b, minX));
        points = ClipEdge(points, p => p.X <= maxX, (a, b) => IntersectX(a, b, maxX));
        points = ClipEdge(points, p => p.Z >= minZ, (a, b) => IntersectZ(a, b, minZ));
        points = ClipEdge(points, p => p.Z <= maxZ, (a, b) => IntersectZ(a, b, maxZ));
        return points;
    }

    private static List<LocalPoint> ClipEdge(List<LocalPoint> input, Func<LocalPoint, bool> isInside, Func<LocalPoint, LocalPoint, LocalPoint> intersect)
    {
        if (input.Count == 0)
            return input;

        var output = new List<LocalPoint>();
        for (var i = 0; i < input.Count; i++)
        {
            var current = input[i];
            var previous = input[(i - 1 + input.Count) % input.Count];
            var currentInside = isInside(current);
            var previousInside = isInside(previous);

            if (currentInside)
            {
                if (!previousInside)
                    output.Add(intersect(previous, current));
                output.Add(current);
            }
            else if (previousInside)
            {
                output.Add(intersect(previous, current));
            }
        }
        return output;
    }

    private static LocalPoint IntersectX(LocalPoint a, LocalPoint b, double x)
    {
        var t = (x - a.X) / (b.X - a.X);
        return new LocalPoint(x, a.Z + t * (b.Z - a.Z));
    }

    private static LocalPoint IntersectZ(LocalPoint a, LocalPoint b, double z)
    {
        var t = (z - a.Z) / (b.Z - a.Z);
        return new LocalPoint(a.X + t * (b.X - a.X), z);
    }
}
