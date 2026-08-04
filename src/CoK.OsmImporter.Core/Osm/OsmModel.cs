namespace CoK.OsmImporter.Core.Osm;

public sealed class OsmNode
{
    public required long Id { get; init; }
    public required double Lat { get; init; }
    public required double Lon { get; init; }
    public IReadOnlyDictionary<string, string> Tags { get; init; } = EmptyTags;

    internal static readonly IReadOnlyDictionary<string, string> EmptyTags =
        new Dictionary<string, string>();

    public bool HasTags => Tags.Count > 0;
}

public sealed class OsmWay
{
    public required long Id { get; init; }
    public required IReadOnlyList<long> NodeIds { get; init; }
    public IReadOnlyDictionary<string, string> Tags { get; init; } = OsmNode.EmptyTags;

    public bool IsClosed => NodeIds.Count > 2 && NodeIds[0] == NodeIds[^1];
}

public enum OsmRelationMemberType
{
    Node,
    Way,
    Relation
}

public sealed record OsmRelationMember(OsmRelationMemberType Type, long Ref, string Role);

public sealed class OsmRelation
{
    public required long Id { get; init; }
    public required IReadOnlyList<OsmRelationMember> Members { get; init; }
    public IReadOnlyDictionary<string, string> Tags { get; init; } = OsmNode.EmptyTags;
}

/// <summary>
/// Parsed OSM document. Nodes/Ways/Relations are keyed by their OSM id for O(1) reference
/// resolution (way -> node coordinates, relation -> member way/node).
/// </summary>
public sealed class OsmDocument
{
    public required IReadOnlyDictionary<long, OsmNode> Nodes { get; init; }
    public required IReadOnlyDictionary<long, OsmWay> Ways { get; init; }
    public required IReadOnlyDictionary<long, OsmRelation> Relations { get; init; }

    /// <summary>Bounding box from the &lt;bounds&gt; element, if present in the file.</summary>
    public Geo.BoundingBox? Bounds { get; init; }
}
