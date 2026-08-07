using CoK.OsmImporter.Core.Geo;

namespace CoK.OsmImporter.Core.Elevation;

/// <summary>
/// Looks up ground elevation (meters above sea level) for a batch of lat/lon points. Abstracted
/// behind an interface so the converter's terrain-generation logic can be unit tested without a
/// real network call — see OpenMeteoElevationProvider for the actual implementation.
/// </summary>
public interface IElevationProvider
{
    /// <summary>
    /// Returns one elevation per input point, in the same order. Implementations may batch
    /// internally; callers should not assume anything about request count/size.
    /// </summary>
    Task<IReadOnlyList<double>> GetElevationsAsync(IReadOnlyList<GeoPoint> points, CancellationToken cancellationToken = default);
}
