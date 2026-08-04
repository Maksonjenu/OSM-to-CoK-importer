using CoK.OsmImporter.Core.Import;
using CoK.OsmImporter.Core.Mapping;

namespace CoK.OsmImporter.Core.Tests;

public class WayClassifierTests
{
    private readonly WayClassifier _classifier = new(MappingConfigLoader.LoadDefault());

    private static Dictionary<string, string> Tags(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void Waterway_TakesPriorityEvenWhenClosed()
    {
        var tags = Tags(("waterway", "river"), ("building", "yes"));

        Assert.Equal(WayFeatureKind.Waterway, _classifier.Classify(tags, isClosed: true));
    }

    [Fact]
    public void WaterPolygon_RequiresClosedWay()
    {
        var tags = Tags(("natural", "water"));

        Assert.Equal(WayFeatureKind.WaterPolygon, _classifier.Classify(tags, isClosed: true));
        Assert.Equal(WayFeatureKind.None, _classifier.Classify(tags, isClosed: false));
    }

    [Fact]
    public void ForestPolygon_BeatsFarmlandAndBarrier()
    {
        var tags = Tags(("natural", "wood"), ("landuse", "farmland"), ("barrier", "fence"));

        Assert.Equal(WayFeatureKind.ForestPolygon, _classifier.Classify(tags, isClosed: true));
    }

    [Fact]
    public void Building_BeatsRoad()
    {
        var tags = Tags(("building", "house"), ("highway", "service"));

        Assert.Equal(WayFeatureKind.Building, _classifier.Classify(tags, isClosed: true));
    }

    [Fact]
    public void Highway_ResolvesToRoad()
    {
        var tags = Tags(("highway", "residential"));

        Assert.Equal(WayFeatureKind.Road, _classifier.Classify(tags, isClosed: false));
    }

    [Fact]
    public void UnknownBarrierValue_DoesNotMatchBarrier()
    {
        var tags = Tags(("barrier", "kerb")); // "kerb" isn't in the default mapping's barrier list

        Assert.Equal(WayFeatureKind.None, _classifier.Classify(tags, isClosed: false));
    }

    [Fact]
    public void NoRecognizedTags_ReturnsNone()
    {
        var tags = Tags(("name", "Something"));

        Assert.Equal(WayFeatureKind.None, _classifier.Classify(tags, isClosed: false));
    }
}
