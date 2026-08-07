using CoK.OsmImporter.Core.AssetCatalog;
using CoK.OsmImporter.Core.Elevation;
using CoK.OsmImporter.Core.Geo;
using CoK.OsmImporter.Core.Import;
using CoK.OsmImporter.Core.Mapping;
using CoK.OsmImporter.Core.Mommap;

namespace CoK.OsmImporter.Core.Tests;

/// <summary>Deterministic stand-in for OpenMeteoElevationProvider — no network access, so the
/// pipeline (grid tiling, baseline/scale math, plateau emission) is testable without it.</summary>
public sealed class FakeElevationProvider(Func<GeoPoint, double> elevationAt) : IElevationProvider
{
    public Task<IReadOnlyList<double>> GetElevationsAsync(IReadOnlyList<GeoPoint> points, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<double>>(points.Select(elevationAt).ToList());
}

public class ElevationTests
{
    private static (MommapDocument Target, OsmToMommapConverter Converter) NewConverter()
    {
        var mapping = MappingConfigLoader.LoadDefault();
        var catalog = AssetCatalog.AssetCatalog.LoadEmbedded();
        return (MommapDocument.CreateBlank(), new OsmToMommapConverter(mapping, catalog));
    }

    private static BoundingBox SmallBbox() => new(53.6800, 88.0500, 53.6820, 88.0540);

    [Fact]
    public async Task AddElevationPlateausAsync_DisabledByDefault_AddsNothing()
    {
        var (target, converter) = NewConverter();
        var options = new ConverterOptions(); // ElevationGridUnits null by default

        var added = await converter.AddElevationPlateausAsync(target, options, SmallBbox(), new FakeElevationProvider(_ => 100.0));

        Assert.Equal(0, added);
        Assert.Empty(target.Paths);
    }

    [Fact]
    public async Task AddElevationPlateausAsync_FlatTerrain_AddsNoPlateaus()
    {
        var (target, converter) = NewConverter();
        var options = new ConverterOptions { ElevationGridUnits = 20.0 };

        // every cell samples the exact same elevation -> baseline == every cell -> height 0 everywhere.
        var added = await converter.AddElevationPlateausAsync(target, options, SmallBbox(), new FakeElevationProvider(_ => 250.0));

        Assert.Equal(0, added);
        Assert.Empty(target.Paths);
    }

    [Fact]
    public async Task AddElevationPlateausAsync_VariedTerrain_AddsClosedPlateausWithConfiguredAsset()
    {
        var (target, converter) = NewConverter();
        var mapping = MappingConfigLoader.LoadDefault();
        var options = new ConverterOptions { ElevationGridUnits = 20.0, ElevationScale = 5.0 };

        // elevation rises with latitude, so the grid definitely isn't flat.
        var added = await converter.AddElevationPlateausAsync(
            target, options, SmallBbox(), new FakeElevationProvider(p => (p.Lat - 53.68) * 100_000));

        Assert.True(added > 0);
        Assert.Equal(added, target.Paths.Count);
        Assert.All(target.Paths, p =>
        {
            Assert.Equal(mapping.Elevation!.Filename, p.Filename);
            Assert.Equal(mapping.Elevation.PathType, p.PathType);
            Assert.True(p.IsClosed);
            Assert.Equal(4, p.PointsObjects.Count); // one square cell = 4 corners
            Assert.True(p.HeightChangingPositionY is > 0);
        });
    }

    [Fact]
    public async Task AddElevationPlateausAsync_HeightIsCappedAt30()
    {
        var (target, converter) = NewConverter();
        // A huge real-world elevation swing (2000m) divided by a small scale must still clamp.
        var options = new ConverterOptions { ElevationGridUnits = 20.0, ElevationScale = 1.0 };

        await converter.AddElevationPlateausAsync(
            target, options, SmallBbox(), new FakeElevationProvider(p => (p.Lat - 53.68) * 2_000_000));

        Assert.NotEmpty(target.Paths);
        Assert.All(target.Paths, p => Assert.True(p.HeightChangingPositionY <= 30.0));
    }

    [Fact]
    public async Task AddElevationPlateausAsync_LowestCellIsRelativeBaseline_NeverNegative()
    {
        var (target, converter) = NewConverter();
        var options = new ConverterOptions { ElevationGridUnits = 20.0, ElevationScale = 5.0 };

        await converter.AddElevationPlateausAsync(
            target, options, SmallBbox(), new FakeElevationProvider(p => (p.Lat - 53.68) * 100_000));

        Assert.All(target.Paths, p => Assert.True(p.HeightChangingPositionY is null or > 0));
    }
}
