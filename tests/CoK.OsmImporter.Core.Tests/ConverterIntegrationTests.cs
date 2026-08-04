using System.Text;
using CoK.OsmImporter.Core.AssetCatalog;
using CoK.OsmImporter.Core.Geo;
using CoK.OsmImporter.Core.Import;
using CoK.OsmImporter.Core.Mapping;
using CoK.OsmImporter.Core.Mommap;
using CoK.OsmImporter.Core.Osm;

namespace CoK.OsmImporter.Core.Tests;

public class ConverterIntegrationTests
{
    private const string SyntheticOsm = """
        <?xml version="1.0" encoding="UTF-8"?>
        <osm version="0.6" generator="test">
         <node id="1" lat="53.6800" lon="88.0500"/>
         <node id="2" lat="53.6810" lon="88.0510"/>
         <node id="10" lat="53.6820" lon="88.0520"/>
         <node id="11" lat="53.6821" lon="88.0520"/>
         <node id="12" lat="53.6821" lon="88.0521"/>
         <node id="13" lat="53.6820" lon="88.0521"/>
         <node id="20" lat="53.6830" lon="88.0530"/>
         <node id="21" lat="53.6831" lon="88.0530"/>
         <node id="22" lat="53.6831" lon="88.0531"/>
         <node id="23" lat="53.6830" lon="88.0531"/>
         <node id="30" lat="53.6850" lon="88.0550"/>
         <node id="31" lat="53.6860" lon="88.0560"/>
         <node id="40" lat="53.6870" lon="88.0570">
          <tag k="natural" v="tree"/>
         </node>
         <node id="50" lat="53.6880" lon="88.0580"/>
         <node id="51" lat="53.6890" lon="88.0590"/>
         <way id="100">
          <nd ref="1"/>
          <nd ref="2"/>
          <tag k="highway" v="residential"/>
         </way>
         <way id="101">
          <nd ref="10"/>
          <nd ref="11"/>
          <nd ref="12"/>
          <nd ref="13"/>
          <nd ref="10"/>
          <tag k="building" v="house"/>
         </way>
         <way id="102">
          <nd ref="20"/>
          <nd ref="21"/>
          <nd ref="22"/>
          <nd ref="23"/>
          <nd ref="20"/>
          <tag k="natural" v="wood"/>
         </way>
         <way id="103">
          <nd ref="30"/>
          <nd ref="31"/>
          <tag k="waterway" v="river"/>
         </way>
         <way id="104">
          <nd ref="50"/>
          <nd ref="51"/>
          <tag k="barrier" v="wall"/>
         </way>
        </osm>
        """;

    private static OsmDocument ParseSynthetic()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(SyntheticOsm));
        return OsmXmlParser.Parse(stream);
    }

    private static (MommapDocument Target, Reporting.ImportSummary Summary) RunConverter(ConverterOptions? options = null)
    {
        var osm = ParseSynthetic();
        var mapping = MappingConfigLoader.LoadDefault();
        var catalog = AssetCatalog.AssetCatalog.LoadEmbedded();
        var converter = new OsmToMommapConverter(mapping, catalog);
        var target = MommapDocument.CreateBlank();

        var summary = converter.Convert(osm, target, options ?? new ConverterOptions());
        return (target, summary);
    }

    [Fact]
    public void Convert_EmitsOneFeaturePerCategory()
    {
        var (target, summary) = RunConverter();

        Assert.Equal(1, summary.Roads);
        Assert.Equal(1, summary.Buildings);
        Assert.Equal(1, summary.ForestPolygons);
        Assert.Equal(1, summary.Rivers);
        Assert.Equal(1, summary.Barriers);
        Assert.Equal(1, summary.Trees);

        Assert.Equal(2, target.Objects.Count); // building + tree
        Assert.Equal(4, target.Paths.Count); // road + forest + river + wall
    }

    [Fact]
    public void Convert_RoadUsesConfiguredResidentialPathType()
    {
        var (target, _) = RunConverter();

        var road = target.Paths.Single(p => p.Filename == "res://Source/World/Paths/pa_road.tscn");
        Assert.Equal(19, road.PathType); // "residential" -> 19 in default-mapping.json
        Assert.False(road.IsClosed);
        Assert.Equal(2, road.MainPoints.Count);
        Assert.Single(road.LinesObjects);
    }

    /// <summary>
    /// Regression test for a real bug found via a hand-drawn road in the CoK editor: CoK does NOT
    /// render road/wall/palisade/fence geometry procedurally from path_type+main_points alone —
    /// the visible mesh is a tile prefab stretched across each straight segment (position =
    /// segment midpoint, rotation = segment heading, scale_x = segment length). Verified formula:
    /// rotation_y = -degrees(atan2(dz, dx)).
    /// </summary>
    [Fact]
    public void Convert_RoadSegmentGetsAStretchedTileObject_MatchingConfirmedCoKFormula()
    {
        var (target, _) = RunConverter();
        var catalog = AssetCatalog.AssetCatalog.LoadEmbedded();

        var road = target.Paths.Single(p => p.Filename == "res://Source/World/Paths/pa_road.tscn");
        var a = road.MainPoints[0];
        var b = road.MainPoints[1];

        var tile = Assert.Single(road.LinesObjects[0].Objects);
        var expectedTile = catalog.GetPath(road.Filename, road.PathType).LineTile!.Value;
        Assert.Equal(expectedTile.Filename, tile.Filename);
        Assert.Equal(expectedTile.ObjectType, tile.ObjectType);

        var dx = b.X - a.X;
        var dz = b.Z - a.Z;
        var expectedLength = Math.Sqrt(dx * dx + dz * dz);
        var expectedRotation = -(Math.Atan2(dz, dx) * 180.0 / Math.PI);

        Assert.Equal((a.X + b.X) / 2.0, tile.PositionX, precision: 6);
        Assert.Equal((a.Z + b.Z) / 2.0, tile.PositionZ, precision: 6);
        Assert.Equal(expectedLength, tile.ScaleX!.Value, precision: 6);
        Assert.Equal(expectedRotation, tile.RotationY!.Value, precision: 6);
        Assert.Equal(0, tile.AssignedPathLineIdx);
    }

    [Fact]
    public void Convert_BarrierSegmentAlsoGetsAStretchedTileObject()
    {
        var (target, _) = RunConverter();

        var wall = target.Paths.Single(p => p.Filename.Contains("pa_stone_wall"));
        var tile = Assert.Single(wall.LinesObjects[0].Objects);
        Assert.Contains("wo_wall_stone", tile.Filename);
        Assert.True(tile.ScaleX > 0);
    }

    /// <summary>
    /// Regression test for a second real bug: rivers need lines_border_points (left/right bank
    /// offset curves) but NOT a placeholder rectangle in lines_objects — confirmed by hand-drawing
    /// both a straight and a curved test river in the CoK editor: both had completely empty
    /// lines_objects on every segment despite the official template.mommap's own rivers having
    /// rectangles there (an older/vestigial quirk, not required — and adding one ourselves
    /// triggered an in-game "path invalid" error).
    /// </summary>
    [Fact]
    public void Convert_RiverSegmentGetsBorderPointsButEmptyLinesObjects()
    {
        var (target, _) = RunConverter();

        var river = target.Paths.Single(p => p.Filename.Contains("paw_river"));

        Assert.All(river.LinesObjects, group => Assert.Empty(group.Objects));

        var width = river.PointsObjects[0].ScaleCustomX!.Value;
        Assert.NotNull(river.LinesBorderPoints);
        var borders = Assert.Single(river.LinesBorderPoints!);
        Assert.Equal(2, borders.PointsLeft.Count);
        Assert.Equal(2, borders.PointsRight.Count);

        // the banks must be parallel to the centerline and offset by exactly half the width
        var leftWidth = Math.Sqrt(Math.Pow(borders.PointsLeft[0].X - borders.PointsRight[0].X, 2)
                                   + Math.Pow(borders.PointsLeft[0].Z - borders.PointsRight[0].Z, 2));
        Assert.Equal(width, leftWidth, precision: 6);
    }

    /// <summary>
    /// Regression test for a real bug: a river spline has no line-tile, so its only width control
    /// is `scale_custom_x` on every vertex placeholder — confirmed against template.mommap, where
    /// every single river vertex has it set (~10 units) and no other feature type does. Without
    /// it a river renders at zero width, i.e. invisible.
    /// </summary>
    [Fact]
    public void Convert_RiverVerticesAllGetScaleCustomXWidth()
    {
        var (target, _) = RunConverter();

        var river = target.Paths.Single(p => p.Filename.Contains("paw_river"));
        Assert.All(river.PointsObjects, po => Assert.True(po.ScaleCustomX is > 0));
    }

    [Fact]
    public void Convert_RiverWidthScalesDownWithMetersPerUnit()
    {
        var (fullScale, _) = RunConverter(new ConverterOptions { MetersPerUnit = 1.0 });
        var (halved, _) = RunConverter(new ConverterOptions { MetersPerUnit = 2.0 });

        var widthAt1 = fullScale.Paths.Single(p => p.Filename.Contains("paw_river")).PointsObjects[0].ScaleCustomX!.Value;
        var widthAt2 = halved.Paths.Single(p => p.Filename.Contains("paw_river")).PointsObjects[0].ScaleCustomX!.Value;

        Assert.Equal(widthAt1 / 2.0, widthAt2, precision: 6);
    }

    [Fact]
    public void Convert_ForestPolygonIsClosedWithDedupedRing()
    {
        var (target, _) = RunConverter();

        var forest = target.Paths.Single(p => p.Filename.Contains("pap_forest"));
        Assert.True(forest.IsClosed);
        Assert.Equal(25, forest.PathType);
        Assert.Equal(5, forest.MainPoints.Count); // 4 unique + closing repeat
        Assert.Equal(4, forest.PointsObjects.Count);
        Assert.Equal(4, forest.LinesObjects.Count);
    }

    /// <summary>
    /// Regression test for a real bug found via a user-provided reference file (a forest zone
    /// hand-drawn, then auto-filled by CoK's own tooling, in the real editor): CoK does NOT
    /// generate a forest plot's tree/grass fill procedurally at load time — it bakes actual object
    /// instances into `area_objects` when the shape is drawn/edited in the editor. A forest plot
    /// with an empty `area_objects` (what every earlier version of this importer emitted) renders
    /// as an empty, invisible zone. Confirmed density from the reference file: ~1 object/unit².
    /// </summary>
    [Fact]
    public void Convert_ForestPolygonGetsBakedAreaFill()
    {
        var (target, _) = RunConverter();
        var catalog = AssetCatalog.AssetCatalog.LoadEmbedded();

        var forest = target.Paths.Single(p => p.Filename.Contains("pap_forest"));

        Assert.NotNull(forest.AreaObjects);
        Assert.NotEmpty(forest.AreaObjects!);

        foreach (var deco in forest.AreaObjects!)
        {
            Assert.Equal(catalog.GetObjectType(deco.Filename), deco.ObjectType);
            Assert.Equal(0, deco.AssignedPathAreaIdx);
            Assert.InRange(deco.ScaleX!.Value, 0.79, 1.21);
        }
    }

    [Fact]
    public void Convert_ForestFillCountRoughlyMatchesConfiguredDensityTimesArea()
    {
        var (target, _) = RunConverter();

        var forest = target.Paths.Single(p => p.Filename.Contains("pap_forest"));
        var ring = forest.PointsObjects.Select(po => new LocalPoint(po.PositionX, po.PositionZ)).ToList();
        var area = Math.Abs(GeometryHelpers.SignedArea(ring));
        var density = MappingConfigLoader.LoadDefault().ForestPolygon.FillDensity!.Value;

        // Rejection sampling won't hit the target exactly for a tiny polygon, but should be close.
        var expected = area * density;
        Assert.InRange(forest.AreaObjects!.Count, expected * 0.5, expected * 1.5 + 5);
    }

    /// <summary>
    /// Regression test for a real bug found after the forest-fill fix: real OSM forest polygons
    /// can trigger an in-game "area too large" glitch. A polygon bigger than MaxPlotAreaUnits must
    /// come back as multiple smaller MapPaths instead of one oversized one, and the summary count
    /// must reflect the actual number of paths emitted (not the original feature count).
    /// </summary>
    [Fact]
    public void Convert_OversizedForestPolygon_SplitsIntoMultiplePaths()
    {
        var (target, summary) = RunConverter(new ConverterOptions { MaxPlotAreaUnits = 10.0 });

        var forestPaths = target.Paths.Where(p => p.Filename.Contains("pap_forest")).ToList();

        Assert.True(forestPaths.Count > 1, "a forest polygon far bigger than a 10-unit² cap should split.");
        Assert.Equal(forestPaths.Count, summary.ForestPolygons);
        Assert.All(forestPaths, p => Assert.True(p.IsClosed));
    }

    /// <summary>Water plots always have area_objects present but empty — their fill is a
    /// shader/mesh effect, not discrete baked objects (confirmed: template.mommap's lake/ocean
    /// plots and the reference file's water plots are all `"area_objects": []`).</summary>
    [Fact]
    public void Convert_WaterPolygonHasEmptyAreaObjects()
    {
        var way = """
            <?xml version="1.0" encoding="UTF-8"?>
            <osm version="0.6" generator="test">
             <node id="1" lat="53.6900" lon="88.0600"/>
             <node id="2" lat="53.6901" lon="88.0600"/>
             <node id="3" lat="53.6901" lon="88.0601"/>
             <node id="4" lat="53.6900" lon="88.0601"/>
             <way id="200">
              <nd ref="1"/><nd ref="2"/><nd ref="3"/><nd ref="4"/><nd ref="1"/>
              <tag k="natural" v="water"/>
             </way>
            </osm>
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(way));
        var osm = OsmXmlParser.Parse(stream);
        var mapping = MappingConfigLoader.LoadDefault();
        var catalog = AssetCatalog.AssetCatalog.LoadEmbedded();
        var converter = new OsmToMommapConverter(mapping, catalog);
        var target = MommapDocument.CreateBlank();
        converter.Convert(osm, target, new ConverterOptions());

        var lake = Assert.Single(target.Paths);
        Assert.NotNull(lake.AreaObjects);
        Assert.Empty(lake.AreaObjects!);
    }

    [Fact]
    public void Convert_BuildingObjectUsesCatalogObjectType()
    {
        var (target, _) = RunConverter();
        var catalog = AssetCatalog.AssetCatalog.LoadEmbedded();

        var building = target.Objects.Single(o => o.Filename.Contains("wo_hut_cottage"));
        Assert.Equal(catalog.GetObjectType(building.Filename), building.ObjectType);
    }

    [Fact]
    public void Convert_BaseProfile_SkipsBuildings()
    {
        var (target, summary) = RunConverter(new ConverterOptions { Profile = ImportProfile.Base });

        Assert.Equal(0, summary.Buildings);
        Assert.Equal(1, summary.BuildingsSkippedByProfile);
        Assert.DoesNotContain(target.Objects, o => o.Filename.Contains("woBuildings"));
    }

    [Fact]
    public void Convert_ReplaceMode_WipesExistingContentFirst()
    {
        var osm = ParseSynthetic();
        var mapping = MappingConfigLoader.LoadDefault();
        var catalog = AssetCatalog.AssetCatalog.LoadEmbedded();
        var converter = new OsmToMommapConverter(mapping, catalog);

        var target = MommapDocument.CreateBlank();
        target.Objects.Add(new MapObject { Filename = "res://leftover.tscn", ObjectType = 999 });

        converter.Convert(osm, target, new ConverterOptions { Merge = MergeMode.Replace });

        Assert.DoesNotContain(target.Objects, o => o.Filename == "res://leftover.tscn");
    }

    [Fact]
    public void Convert_AssignsIncreasingUidsAndUpdatesLastUid()
    {
        var (target, _) = RunConverter();

        var uids = target.Paths.Select(p => p.ClickAndPlacingWo.Uid).ToList();
        Assert.Equal(uids.Distinct().Count(), uids.Count); // all unique
        Assert.Equal(uids.Max(), target.Info.LastUid);
    }
}
