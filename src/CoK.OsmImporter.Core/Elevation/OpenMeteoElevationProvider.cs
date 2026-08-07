using System.Globalization;
using System.Text.Json.Serialization;
using CoK.OsmImporter.Core.Geo;

namespace CoK.OsmImporter.Core.Elevation;

/// <summary>
/// Free, no-API-key elevation lookup via <see href="https://open-meteo.com/en/docs/elevation-api"/>
/// (backed by Copernicus DEM GLO-90, ~90m resolution). Picked over paid alternatives (TessaDEM,
/// Google) for a first pass at terrain support — no signup/billing friction, batched requests, at
/// the cost of coarser resolution and no bulk-grid endpoint (unlike TessaDEM's "area" mode) — see
/// TODO.md for that tradeoff.
/// </summary>
public sealed class OpenMeteoElevationProvider : IElevationProvider
{
    private const int BatchSize = 100;
    private readonly HttpClient _httpClient;

    public OpenMeteoElevationProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<IReadOnlyList<double>> GetElevationsAsync(IReadOnlyList<GeoPoint> points, CancellationToken cancellationToken = default)
    {
        var results = new double[points.Count];
        for (var offset = 0; offset < points.Count; offset += BatchSize)
        {
            var batch = points.Skip(offset).Take(BatchSize).ToList();
            var batchResults = await FetchBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            for (var i = 0; i < batch.Count; i++)
                results[offset + i] = batchResults[i];
        }
        return results;
    }

    private async Task<IReadOnlyList<double>> FetchBatchAsync(IReadOnlyList<GeoPoint> batch, CancellationToken cancellationToken)
    {
        var ci = CultureInfo.InvariantCulture;
        var lats = string.Join(',', batch.Select(p => p.Lat.ToString(ci)));
        var lons = string.Join(',', batch.Select(p => p.Lon.ToString(ci)));
        var url = $"https://api.open-meteo.com/v1/elevation?latitude={lats}&longitude={lons}";

        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await System.Text.Json.JsonSerializer.DeserializeAsync<ElevationResponse>(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (body?.Elevation is not { } elevations || elevations.Count != batch.Count)
            throw new InvalidOperationException(
                $"Open-Meteo elevation response didn't match the request: expected {batch.Count} values, " +
                $"got {body?.Elevation?.Count.ToString() ?? "none"}.");

        return elevations;
    }

    private sealed class ElevationResponse
    {
        [JsonPropertyName("elevation")]
        public List<double>? Elevation { get; init; }
    }
}
