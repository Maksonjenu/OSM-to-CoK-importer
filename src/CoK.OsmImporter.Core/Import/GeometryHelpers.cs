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
    /// Turns a centerline (e.g. a river) into a closed ring by offsetting it left/right by half
    /// <paramref name="width"/> — a simple stroke-to-fill "buffer" with flat end caps (a mitered
    /// join at interior vertices: the offset direction is the average of the two adjacent segment
    /// normals). Meant to let a river be drawn as a CoK water *plot* (same procedural fill as
    /// lakes) instead of the river *path*/spline type, which chokes on real OSM geometry
    /// ("path invalid") — plots have proven robust against complex, many-point OSM shapes (lake
    /// polygons import fine), so representing a river as a long thin plot sidesteps the problem
    /// instead of fixing the spline path format.
    /// </summary>
    public static List<LocalPoint> BuildBufferPolygon(IReadOnlyList<LocalPoint> centerline, double width)
    {
        if (centerline.Count < 2)
            return centerline.ToList();

        var halfWidth = width / 2.0;
        var n = centerline.Count;
        var left = new LocalPoint[n];
        var right = new LocalPoint[n];

        for (var i = 0; i < n; i++)
        {
            double nx = 0, nz = 0;
            if (i > 0)
            {
                var (sx, sz) = SegmentNormal(centerline[i - 1], centerline[i]);
                nx += sx;
                nz += sz;
            }
            if (i < n - 1)
            {
                var (sx, sz) = SegmentNormal(centerline[i], centerline[i + 1]);
                nx += sx;
                nz += sz;
            }

            var len = Math.Sqrt(nx * nx + nz * nz);
            if (len < 1e-9)
            {
                nx = 0;
                nz = 1;
                len = 1;
            }
            nx /= len;
            nz /= len;

            var p = centerline[i];
            left[i] = new LocalPoint(p.X + nx * halfWidth, p.Z + nz * halfWidth);
            right[i] = new LocalPoint(p.X - nx * halfWidth, p.Z - nz * halfWidth);
        }

        var ring = new List<LocalPoint>(n * 2);
        ring.AddRange(left);
        for (var i = n - 1; i >= 0; i--)
            ring.Add(right[i]);
        return ring;
    }

    private static (double Nx, double Nz) SegmentNormal(LocalPoint a, LocalPoint b)
    {
        var dx = b.X - a.X;
        var dz = b.Z - a.Z;
        var len = Math.Sqrt(dx * dx + dz * dz);
        if (len < 1e-9)
            return (0, 1);
        return (-dz / len, dx / len);
    }

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

    /// <summary>
    /// Approximates CoK's own "crop row" fill for a farmland Plot: evenly-spaced parallel line
    /// segments at <paramref name="angleDegrees"/> (same rotation convention as everywhere else —
    /// <c>rotation = -atan2(dz, dx) * 180/pi</c>), spaced <paramref name="spacing"/> apart
    /// perpendicular to that direction, each clipped down to just the parts that fall inside
    /// <paramref name="ring"/>. A row that crosses a non-convex bite out of the polygon comes back
    /// as multiple separate segments rather than one that cuts through empty space.
    /// </summary>
    public static List<(LocalPoint A, LocalPoint B)> GenerateParallelRows(IReadOnlyList<LocalPoint> ring, double angleDegrees, double spacing)
    {
        var result = new List<(LocalPoint, LocalPoint)>();
        if (ring.Count < 3 || spacing <= 0)
            return result;

        var rad = -angleDegrees * Math.PI / 180.0;
        var dirX = Math.Cos(rad);
        var dirZ = Math.Sin(rad);
        var perpX = -dirZ;
        var perpZ = dirX;

        var minX = ring.Min(p => p.X);
        var maxX = ring.Max(p => p.X);
        var minZ = ring.Min(p => p.Z);
        var maxZ = ring.Max(p => p.Z);
        var cx = (minX + maxX) / 2.0;
        var cz = (minZ + maxZ) / 2.0;
        var halfSpan = Math.Sqrt(Math.Pow(maxX - minX, 2) + Math.Pow(maxZ - minZ, 2)) / 2.0 + spacing;

        var corners = new (double X, double Z)[] { (minX, minZ), (maxX, minZ), (minX, maxZ), (maxX, maxZ) };
        var offsets = corners.Select(c => (c.X - cx) * perpX + (c.Z - cz) * perpZ);
        var minOffset = offsets.Min();
        var maxOffset = offsets.Max();

        for (var offset = minOffset; offset <= maxOffset; offset += spacing)
        {
            var baseX = cx + perpX * offset;
            var baseZ = cz + perpZ * offset;
            var a = new LocalPoint(baseX - dirX * halfSpan, baseZ - dirZ * halfSpan);
            var b = new LocalPoint(baseX + dirX * halfSpan, baseZ + dirZ * halfSpan);
            result.AddRange(ClipSegmentToPolygon(a, b, ring));
        }

        return result;
    }

    /// <summary>
    /// Finds the sub-spans of segment a-b that lie inside <paramref name="ring"/>, by intersecting
    /// with every ring edge and keeping the spans whose midpoint tests inside. Correct for any
    /// simple (possibly non-convex) polygon.
    /// </summary>
    private static List<(LocalPoint, LocalPoint)> ClipSegmentToPolygon(LocalPoint a, LocalPoint b, IReadOnlyList<LocalPoint> ring)
    {
        var dx = b.X - a.X;
        var dz = b.Z - a.Z;
        var ts = new List<double> { 0.0, 1.0 };
        var n = ring.Count;
        for (var i = 0; i < n; i++)
        {
            var e0 = ring[i];
            var e1 = ring[(i + 1) % n];
            var ex = e1.X - e0.X;
            var ez = e1.Z - e0.Z;
            var denom = dx * ez - dz * ex;
            if (Math.Abs(denom) < 1e-9)
                continue; // parallel to this edge

            var t = ((e0.X - a.X) * ez - (e0.Z - a.Z) * ex) / denom;
            var s = ((e0.X - a.X) * dz - (e0.Z - a.Z) * dx) / denom;
            if (s is >= -1e-9 and <= 1 + 1e-9 && t is >= 0 and <= 1)
                ts.Add(Math.Clamp(t, 0, 1));
        }

        ts.Sort();
        var result = new List<(LocalPoint, LocalPoint)>();
        for (var i = 0; i < ts.Count - 1; i++)
        {
            var t0 = ts[i];
            var t1 = ts[i + 1];
            if (t1 - t0 < 1e-6)
                continue;

            var midT = (t0 + t1) / 2.0;
            if (!IsPointInPolygon(ring, a.X + midT * dx, a.Z + midT * dz))
                continue;

            result.Add((
                new LocalPoint(a.X + t0 * dx, a.Z + t0 * dz),
                new LocalPoint(a.X + t1 * dx, a.Z + t1 * dz)));
        }

        return result;
    }
}
