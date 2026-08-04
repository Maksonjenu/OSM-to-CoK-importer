using CoK.OsmImporter.Core.AssetCatalog;

namespace CoK.OsmImporter.Core.Tests;

public class AssetCatalogTests
{
    [Fact]
    public void LoadEmbedded_ResolvesKnownTreePrefab()
    {
        var catalog = AssetCatalog.AssetCatalog.LoadEmbedded();

        var type = catalog.GetObjectType("res://Source/World/WorldObjects/woDecos/wo_tree_a.tscn");

        Assert.Equal(3, type);
    }

    [Fact]
    public void LoadEmbedded_ResolvesKnownRoadPathVariant()
    {
        var catalog = AssetCatalog.AssetCatalog.LoadEmbedded();

        var asset = catalog.GetPath("res://Source/World/Paths/pa_road.tscn", 23);

        Assert.False(asset.IsClosed);
    }

    [Fact]
    public void LoadEmbedded_ResolvesForestPlotAsClosed()
    {
        var catalog = AssetCatalog.AssetCatalog.LoadEmbedded();

        var asset = catalog.GetPath("res://Source/World/Plots/pap_forest.tscn", 25);

        Assert.True(asset.IsClosed);
    }

    [Fact]
    public void GetObjectType_UnknownFilename_Throws()
    {
        var catalog = AssetCatalog.AssetCatalog.LoadEmbedded();

        Assert.Throws<AssetNotFoundException>(() => catalog.GetObjectType("res://nope.tscn"));
    }
}
