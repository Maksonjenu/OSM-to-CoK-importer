namespace CoK.OsmImporter.Core.Geo;

/// <summary>A lat/lon bounding box used to clip which OSM features get imported.</summary>
public readonly record struct BoundingBox(double MinLat, double MinLon, double MaxLat, double MaxLon)
{
    public GeoPoint Center => new((MinLat + MaxLat) / 2.0, (MinLon + MaxLon) / 2.0);

    public bool Contains(GeoPoint p) =>
        p.Lat >= MinLat && p.Lat <= MaxLat && p.Lon >= MinLon && p.Lon <= MaxLon;

    /// <summary>Builds a bbox from a center point and a radius in meters (equirectangular approx).</summary>
    public static BoundingBox FromCenterRadius(GeoPoint center, double radiusMeters)
    {
        const double metersPerDegreeLat = 111_320.0;
        var latDelta = radiusMeters / metersPerDegreeLat;
        var lonDelta = radiusMeters / (metersPerDegreeLat * Math.Cos(DegToRad(center.Lat)));
        return new BoundingBox(
            center.Lat - latDelta, center.Lon - lonDelta,
            center.Lat + latDelta, center.Lon + lonDelta);
    }

    public static BoundingBox Union(BoundingBox a, BoundingBox b) => new(
        Math.Min(a.MinLat, b.MinLat), Math.Min(a.MinLon, b.MinLon),
        Math.Max(a.MaxLat, b.MaxLat), Math.Max(a.MaxLon, b.MaxLon));

    public double WidthMeters =>
        (MaxLon - MinLon) * 111_320.0 * Math.Cos(DegToRad(Center.Lat));

    public double HeightMeters => (MaxLat - MinLat) * 111_320.0;

    private static double DegToRad(double deg) => deg * Math.PI / 180.0;
}
