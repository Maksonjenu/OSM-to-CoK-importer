namespace CoK.OsmImporter.Core.Geo;

/// <summary>
/// Projects lat/lon to a local, metric X/Z plane centered on an origin point. Good enough for
/// city-sized areas (equirectangular approximation with a cos(lat0) longitude correction) — CoK
/// maps have no real-world geo-reference of their own, so any local-tangent-plane projection is
/// as valid as any other.
/// </summary>
public sealed class EquirectangularProjector
{
    private const double MetersPerDegreeLat = 111_320.0;

    private readonly GeoPoint _origin;
    private readonly double _metersPerDegreeLon;
    private readonly double _unitsPerMeter;

    /// <param name="origin">Lat/lon that maps to local (0, 0).</param>
    /// <param name="metersPerUnit">
    /// How many real-world meters one CoK map unit represents. Default 1.0 (1 unit = 1 meter,
    /// matching the ~1.0-1.2 scale_x/scale_z seen on template buildings). Values &gt; 1 shrink
    /// the imported area onto the canvas, values &lt; 1 enlarge it.
    /// </param>
    public EquirectangularProjector(GeoPoint origin, double metersPerUnit = 1.0)
    {
        if (metersPerUnit <= 0)
            throw new ArgumentOutOfRangeException(nameof(metersPerUnit), "Must be positive.");

        _origin = origin;
        _metersPerDegreeLon = MetersPerDegreeLat * Math.Cos(DegToRad(origin.Lat));
        _unitsPerMeter = 1.0 / metersPerUnit;
    }

    /// <summary>Projects to local units. X = east/west, Z = south/north (OSM north = -Z, matching
    /// the mommap convention where north is "up" on the printed map / -Z in Godot).</summary>
    public LocalPoint Project(GeoPoint p)
    {
        var dLat = p.Lat - _origin.Lat;
        var dLon = p.Lon - _origin.Lon;
        var eastMeters = dLon * _metersPerDegreeLon;
        var northMeters = dLat * MetersPerDegreeLat;
        return new LocalPoint(eastMeters * _unitsPerMeter, -northMeters * _unitsPerMeter);
    }

    private static double DegToRad(double deg) => deg * Math.PI / 180.0;
}
