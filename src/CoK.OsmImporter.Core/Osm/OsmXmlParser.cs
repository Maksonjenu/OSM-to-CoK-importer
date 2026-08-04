using System.Globalization;
using System.Xml;
using CoK.OsmImporter.Core.Geo;

namespace CoK.OsmImporter.Core.Osm;

/// <summary>Streaming parser for OSM XML (.osm) exports, per https://wiki.openstreetmap.org/wiki/OSM_XML.</summary>
public static class OsmXmlParser
{
    public static OsmDocument Parse(string path)
    {
        using var stream = File.OpenRead(path);
        return Parse(stream);
    }

    public static OsmDocument Parse(Stream stream)
    {
        var nodes = new Dictionary<long, OsmNode>();
        var ways = new Dictionary<long, OsmWay>();
        var relations = new Dictionary<long, OsmRelation>();
        BoundingBox? bounds = null;

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        };
        using var reader = XmlReader.Create(stream, settings);

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
                continue;

            switch (reader.Name)
            {
                case "bounds":
                    bounds = ReadBounds(reader);
                    break;
                case "node":
                    var node = ReadNode(reader);
                    nodes[node.Id] = node;
                    break;
                case "way":
                    var way = ReadWay(reader);
                    ways[way.Id] = way;
                    break;
                case "relation":
                    var relation = ReadRelation(reader);
                    relations[relation.Id] = relation;
                    break;
            }
        }

        return new OsmDocument
        {
            Nodes = nodes,
            Ways = ways,
            Relations = relations,
            Bounds = bounds,
        };
    }

    private static BoundingBox ReadBounds(XmlReader reader) => new(
        MinLat: ParseDouble(reader.GetAttribute("minlat")),
        MinLon: ParseDouble(reader.GetAttribute("minlon")),
        MaxLat: ParseDouble(reader.GetAttribute("maxlat")),
        MaxLon: ParseDouble(reader.GetAttribute("maxlon")));

    private static OsmNode ReadNode(XmlReader reader)
    {
        var id = ParseLong(reader.GetAttribute("id"));
        var lat = ParseDouble(reader.GetAttribute("lat"));
        var lon = ParseDouble(reader.GetAttribute("lon"));
        var tags = ReadTagsIfAny(reader, "node");
        return new OsmNode { Id = id, Lat = lat, Lon = lon, Tags = tags };
    }

    private static OsmWay ReadWay(XmlReader reader)
    {
        var id = ParseLong(reader.GetAttribute("id"));
        var nodeIds = new List<long>();
        var tags = new Dictionary<string, string>();

        if (reader.IsEmptyElement)
            return new OsmWay { Id = id, NodeIds = nodeIds, Tags = tags };

        var depth = reader.Depth;
        while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
        {
            if (reader.NodeType != XmlNodeType.Element)
                continue;

            if (reader.Name == "nd")
            {
                nodeIds.Add(ParseLong(reader.GetAttribute("ref")));
            }
            else if (reader.Name == "tag")
            {
                var (k, v) = ReadTagAttributes(reader);
                tags[k] = v;
            }
        }

        return new OsmWay { Id = id, NodeIds = nodeIds, Tags = tags };
    }

    private static OsmRelation ReadRelation(XmlReader reader)
    {
        var id = ParseLong(reader.GetAttribute("id"));
        var members = new List<OsmRelationMember>();
        var tags = new Dictionary<string, string>();

        if (reader.IsEmptyElement)
            return new OsmRelation { Id = id, Members = members, Tags = tags };

        var depth = reader.Depth;
        while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
        {
            if (reader.NodeType != XmlNodeType.Element)
                continue;

            if (reader.Name == "member")
            {
                var type = reader.GetAttribute("type") switch
                {
                    "node" => OsmRelationMemberType.Node,
                    "way" => OsmRelationMemberType.Way,
                    "relation" => OsmRelationMemberType.Relation,
                    var other => throw new FormatException($"Unknown relation member type '{other}'."),
                };
                var memberRef = ParseLong(reader.GetAttribute("ref"));
                var role = reader.GetAttribute("role") ?? string.Empty;
                members.Add(new OsmRelationMember(type, memberRef, role));
            }
            else if (reader.Name == "tag")
            {
                var (k, v) = ReadTagAttributes(reader);
                tags[k] = v;
            }
        }

        return new OsmRelation { Id = id, Members = members, Tags = tags };
    }

    /// <summary>Reads the &lt;tag&gt; children of the current element (node/way/relation), if any.</summary>
    private static IReadOnlyDictionary<string, string> ReadTagsIfAny(XmlReader reader, string parentName)
    {
        if (reader.IsEmptyElement)
            return OsmNode.EmptyTags;

        var tags = new Dictionary<string, string>();
        var depth = reader.Depth;
        while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Name == "tag")
            {
                var (k, v) = ReadTagAttributes(reader);
                tags[k] = v;
            }
        }

        return tags.Count == 0 ? OsmNode.EmptyTags : tags;
    }

    private static (string Key, string Value) ReadTagAttributes(XmlReader reader) =>
        (reader.GetAttribute("k") ?? string.Empty, reader.GetAttribute("v") ?? string.Empty);

    private static double ParseDouble(string? s) =>
        double.Parse(s ?? throw new FormatException("Missing expected numeric attribute."), CultureInfo.InvariantCulture);

    private static long ParseLong(string? s) =>
        long.Parse(s ?? throw new FormatException("Missing expected id/ref attribute."), CultureInfo.InvariantCulture);
}
