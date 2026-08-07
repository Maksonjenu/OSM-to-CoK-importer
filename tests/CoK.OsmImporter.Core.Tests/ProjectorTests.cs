using CoK.OsmImporter.Core.Geo;

namespace CoK.OsmImporter.Core.Tests;

public class ProjectorTests
{
    [Fact]
    public void Project_Origin_ReturnsZero()
    {
        var origin = new GeoPoint(53.68, 88.05);
        var projector = new EquirectangularProjector(origin);

        var result = projector.Project(origin);

        Assert.Equal(0, result.X, precision: 6);
        Assert.Equal(0, result.Z, precision: 6);
    }

    [Fact]
    public void Project_OneDegreeNorth_IsAbout111KmNegativeZ()
    {
        var origin = new GeoPoint(0, 0);
        var projector = new EquirectangularProjector(origin);

        var result = projector.Project(new GeoPoint(1, 0));

        // North increases latitude; convention here is north = -Z.
        Assert.Equal(-111_320.0, result.Z, precision: 0);
        Assert.Equal(0, result.X, precision: 6);
    }

    [Fact]
    public void Project_OneDegreeEastAtEquator_IsAbout111KmPositiveX()
    {
        var origin = new GeoPoint(0, 0);
        var projector = new EquirectangularProjector(origin);

        var result = projector.Project(new GeoPoint(0, 1));

        Assert.Equal(111_320.0, result.X, precision: 0);
        Assert.Equal(0, result.Z, precision: 6);
    }

    [Fact]
    public void Project_EastAtHighLatitude_ShrinksWithCosine()
    {
        // At 60N, a degree of longitude is about half the ground distance of a degree at the equator.
        var origin = new GeoPoint(60, 0);
        var projector = new EquirectangularProjector(origin);

        var result = projector.Project(new GeoPoint(60, 1));

        Assert.True(result.X is > 50_000 and < 60_000, $"expected ~55.6km, got {result.X}");
    }

    [Fact]
    public void MetersPerUnit_Scales_TheOutput()
    {
        var origin = new GeoPoint(0, 0);
        var oneToOne = new EquirectangularProjector(origin, metersPerUnit: 1.0);
        var tenToOne = new EquirectangularProjector(origin, metersPerUnit: 10.0);

        var target = new GeoPoint(0, 1);
        var a = oneToOne.Project(target);
        var b = tenToOne.Project(target);

        Assert.Equal(a.X / 10.0, b.X, precision: 3);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveScale()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EquirectangularProjector(new GeoPoint(0, 0), metersPerUnit: 0));
    }

    [Theory]
    [InlineData(53.68, 88.05, 1.0)]
    [InlineData(0.0, 0.0, 1.0)]
    [InlineData(60.0, 30.0, 10.0)]
    public void Unproject_IsTheInverseOfProject(double lat, double lon, double metersPerUnit)
    {
        var origin = new GeoPoint(lat, lon);
        var projector = new EquirectangularProjector(origin, metersPerUnit);
        var target = new GeoPoint(lat + 0.01, lon - 0.02);

        var roundTripped = projector.Unproject(projector.Project(target));

        Assert.Equal(target.Lat, roundTripped.Lat, precision: 9);
        Assert.Equal(target.Lon, roundTripped.Lon, precision: 9);
    }
}
