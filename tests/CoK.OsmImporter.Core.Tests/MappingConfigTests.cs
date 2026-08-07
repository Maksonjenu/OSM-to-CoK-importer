using CoK.OsmImporter.Core.AssetCatalog;
using CoK.OsmImporter.Core.Mapping;

namespace CoK.OsmImporter.Core.Tests;

public class MappingConfigTests
{
    [Fact]
    public void DefaultMapping_LoadsAndDeserializes()
    {
        var config = MappingConfigLoader.LoadDefault();

        Assert.NotEmpty(config.Roads);
        Assert.NotEmpty(config.Trees);
        Assert.NotEmpty(config.Buildings.Default);
        Assert.True(config.Roads.ContainsKey("default"), "roads must define a 'default' fallback.");
    }

    /// <summary>
    /// Every filename the shipped default-mapping.json references must actually exist in the
    /// asset catalog extracted from template.mommap — this is exactly the failure mode
    /// AssetCatalog is meant to catch, applied to our own defaults so a typo never ships.
    /// </summary>
    [Fact]
    public void DefaultMapping_AllReferencedAssetsExistInCatalog()
    {
        var config = MappingConfigLoader.LoadDefault();
        var catalog = AssetCatalog.AssetCatalog.LoadEmbedded();

        foreach (var tree in config.Trees)
            catalog.GetObjectType(tree.Filename);

        foreach (var barrier in config.Barriers.Values)
            catalog.GetPath(barrier.Filename, barrier.PathType);

        foreach (var road in config.Roads.Values)
            catalog.GetPath(config.RoadFilename, road.PathType);

        if (config.Bridge is { } bridge)
            catalog.GetPath(bridge.Filename, bridge.PathType);

        catalog.GetPath(config.Waterway.Asset.Filename, config.Waterway.Asset.PathType);
        catalog.GetPath(config.WaterPolygon.Asset.Filename, config.WaterPolygon.Asset.PathType);
        catalog.GetPath(config.ForestPolygon.Asset.Filename, config.ForestPolygon.Asset.PathType);
        catalog.GetPath(config.FarmlandPolygon.Asset.Filename, config.FarmlandPolygon.Asset.PathType);

        foreach (var rule in new[] { config.Waterway, config.WaterPolygon, config.ForestPolygon, config.FarmlandPolygon })
            foreach (var fillAsset in rule.Fill)
                catalog.GetObjectType(fillAsset.Filename);

        foreach (var asset in config.Buildings.Default)
            catalog.GetObjectType(asset.Filename);

        foreach (var rule in config.Buildings.Rules)
            foreach (var asset in rule.Assets)
                catalog.GetObjectType(asset.Filename);
    }

    [Fact]
    public void DefaultMapping_ForestPolygonHasFillConfigured()
    {
        var config = MappingConfigLoader.LoadDefault();

        Assert.NotEmpty(config.ForestPolygon.Fill);
        Assert.True(config.ForestPolygon.FillDensity is > 0);
    }

    [Fact]
    public void DefaultMapping_BridgeIsConfiguredAndDistinctFromPlainRoad()
    {
        var config = MappingConfigLoader.LoadDefault();

        Assert.NotNull(config.Bridge);
        Assert.NotEqual(config.RoadFilename, config.Bridge!.Filename);
    }
}
