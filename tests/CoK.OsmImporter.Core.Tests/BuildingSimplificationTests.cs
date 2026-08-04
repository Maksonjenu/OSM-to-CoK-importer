using System.Text;
using CoK.OsmImporter.Core.AssetCatalog;
using CoK.OsmImporter.Core.Geo;
using CoK.OsmImporter.Core.Import;
using CoK.OsmImporter.Core.Mapping;
using CoK.OsmImporter.Core.Mommap;
using CoK.OsmImporter.Core.Osm;

namespace CoK.OsmImporter.Core.Tests;

/// <summary>
/// Regression test for a real bug: line-simplification (added to fix "path invalid" errors on
/// dense OSM roads/rivers) was being applied to EVERY way, including buildings. A small building
/// footprint (often under 1 map unit across at a heavily-compressed --scale) would collapse to 2
/// points under the same tolerance that's reasonable for a 100-point road, zeroing out
/// DominantEdgeAngleDegrees and leaving the building facing a default direction instead of
/// matching its actual footprint orientation.
/// </summary>
public class BuildingSimplificationTests
{
    // Two identically-shaped (30°-rotated) rectangular footprints, one ~1m x 0.6m (smaller than
    // the default 2.0-unit simplification tolerance) and one ~100m x 60m (unaffected by it) —
    // far apart so they're classified independently. If simplification incorrectly touches
    // buildings, the small one's rotation collapses while the large one's doesn't.
    private const string SyntheticOsm = """
        <?xml version="1.0" encoding="UTF-8"?>
        <osm version="0.6" generator="test">
         <node id="1" lat="53.6820000" lon="88.0520000"/>
         <node id="2" lat="53.6820045" lon="88.0520131"/>
         <node id="3" lat="53.6820092" lon="88.0520086"/>
         <node id="4" lat="53.6820047" lon="88.0519954"/>
         <node id="11" lat="53.6840000" lon="88.0560000"/>
         <node id="12" lat="53.6844492" lon="88.0573136"/>
         <node id="13" lat="53.6849159" lon="88.0568586"/>
         <node id="14" lat="53.6844668" lon="88.0555450"/>
         <way id="100">
          <nd ref="1"/>
          <nd ref="2"/>
          <nd ref="3"/>
          <nd ref="4"/>
          <nd ref="1"/>
          <tag k="building" v="house"/>
         </way>
         <way id="101">
          <nd ref="11"/>
          <nd ref="12"/>
          <nd ref="13"/>
          <nd ref="14"/>
          <nd ref="11"/>
          <tag k="building" v="house"/>
         </way>
        </osm>
        """;

    [Fact]
    public void Convert_SmallBuildingRotation_MatchesLargeBuildingOfSameShape_DespiteSimplification()
    {
        // Each building is imported in its own tightly-scoped run (own bbox -> own projector
        // origin) so the two results are unambiguous. Rotation is a purely local measurement of
        // edge direction, so it's unaffected by the different origins — the two runs' angles are
        // directly comparable.
        var small = ConvertSingleBuilding(new BoundingBox(53.6819, 88.05195, 53.6821, 88.05205));
        var large = ConvertSingleBuilding(new BoundingBox(53.6835, 88.0550, 53.6850, 88.0575));

        // Default SimplificationUnits (2.0) is bigger than the small building's ~1m footprint but
        // irrelevant to the large one's ~100m footprint — exactly the scenario that broke: without
        // the fix, `small`'s footprint collapses under simplification and its rotation defaults to 0.
        Assert.NotEqual(0.0, small.RotationY!.Value, tolerance: 0.01);

        // A rectangle's two "longest edge" candidates are parallel but point opposite ways, so the
        // heuristic's result is only meaningful modulo 180°.
        var diff = Math.Abs(large.RotationY!.Value - small.RotationY!.Value) % 180.0;
        var wrapped = Math.Min(diff, 180.0 - diff);
        Assert.True(wrapped < 0.5, $"expected angles to match mod 180 (within 0.5°), got large={large.RotationY}, small={small.RotationY}, diff mod 180={wrapped}");
    }

    private static MapObject ConvertSingleBuilding(BoundingBox bbox)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(SyntheticOsm));
        var osm = OsmXmlParser.Parse(stream);
        var mapping = MappingConfigLoader.LoadDefault();
        var catalog = AssetCatalog.AssetCatalog.LoadEmbedded();
        var converter = new OsmToMommapConverter(mapping, catalog);
        var target = MommapDocument.CreateBlank();

        converter.Convert(osm, target, new ConverterOptions { Bbox = bbox });

        return Assert.Single(target.Objects);
    }
}
