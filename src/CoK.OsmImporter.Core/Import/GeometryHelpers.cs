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
}
