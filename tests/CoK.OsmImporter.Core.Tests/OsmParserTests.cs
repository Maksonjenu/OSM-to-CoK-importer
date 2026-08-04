using System.Text;
using CoK.OsmImporter.Core.Osm;

namespace CoK.OsmImporter.Core.Tests;

public class OsmParserTests
{
    private const string SampleXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <osm version="0.6" generator="test">
         <bounds minlat="10.0" minlon="20.0" maxlat="11.0" maxlon="21.0"/>
         <node id="1" lat="10.1" lon="20.1"/>
         <node id="2" lat="10.2" lon="20.2">
          <tag k="natural" v="tree"/>
          <tag k="leaf_type" v="broadleaved"/>
         </node>
         <node id="3" lat="10.3" lon="20.3"/>
         <node id="4" lat="10.4" lon="20.4"/>
         <way id="100">
          <nd ref="1"/>
          <nd ref="2"/>
          <nd ref="3"/>
          <tag k="highway" v="residential"/>
          <tag k="name" v="Test Street"/>
         </way>
         <way id="101">
          <nd ref="1"/>
          <nd ref="2"/>
          <nd ref="3"/>
          <nd ref="4"/>
          <nd ref="1"/>
          <tag k="natural" v="wood"/>
         </way>
         <relation id="200">
          <member type="way" ref="101" role="outer"/>
          <tag k="type" v="multipolygon"/>
          <tag k="natural" v="water"/>
         </relation>
        </osm>
        """;

    private static OsmDocument Parse(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return OsmXmlParser.Parse(stream);
    }

    [Fact]
    public void Parse_ReadsBounds()
    {
        var doc = Parse(SampleXml);

        Assert.NotNull(doc.Bounds);
        Assert.Equal(10.0, doc.Bounds!.Value.MinLat);
        Assert.Equal(21.0, doc.Bounds!.Value.MaxLon);
    }

    [Fact]
    public void Parse_ReadsNodesWithAndWithoutTags()
    {
        var doc = Parse(SampleXml);

        Assert.Equal(4, doc.Nodes.Count);
        Assert.False(doc.Nodes[1].HasTags);
        Assert.True(doc.Nodes[2].HasTags);
        Assert.Equal("tree", doc.Nodes[2].Tags["natural"]);
        Assert.Equal(10.2, doc.Nodes[2].Lat);
        Assert.Equal(20.2, doc.Nodes[2].Lon);
    }

    [Fact]
    public void Parse_ReadsWayNodeRefsAndTags()
    {
        var doc = Parse(SampleXml);

        var way = doc.Ways[100];
        Assert.Equal([1L, 2L, 3L], way.NodeIds);
        Assert.Equal("residential", way.Tags["highway"]);
        Assert.False(way.IsClosed);
    }

    [Fact]
    public void Parse_DetectsClosedWay()
    {
        var doc = Parse(SampleXml);

        var way = doc.Ways[101];
        Assert.True(way.IsClosed);
        Assert.Equal(5, way.NodeIds.Count);
    }

    [Fact]
    public void Parse_ReadsRelationMembersAndTags()
    {
        var doc = Parse(SampleXml);

        var relation = doc.Relations[200];
        Assert.Single(relation.Members);
        Assert.Equal(OsmRelationMemberType.Way, relation.Members[0].Type);
        Assert.Equal(101, relation.Members[0].Ref);
        Assert.Equal("outer", relation.Members[0].Role);
        Assert.Equal("multipolygon", relation.Tags["type"]);
        Assert.Equal("water", relation.Tags["natural"]);
    }

    // mejka_map.osm is an optional, gitignored, bring-your-own file (see README.md) — no-ops if
    // nobody's dropped one in.
    [Fact]
    public void Parse_RealSampleFile_DoesNotThrowAndFindsSaneCounts()
    {
        if (TestPaths.SampleOsm is not { } path)
            return;

        var doc = OsmXmlParser.Parse(path);

        Assert.True(doc.Nodes.Count > 0);
        Assert.True(doc.Ways.Count > 0);
    }
}
