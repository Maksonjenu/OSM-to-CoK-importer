using CoK.OsmImporter.Core.AssetCatalog;
using CoK.OsmImporter.Core.Geo;
using CoK.OsmImporter.Core.Mapping;
using CoK.OsmImporter.Core.Mommap;
using CoK.OsmImporter.Core.Osm;
using CoK.OsmImporter.Core.Reporting;

namespace CoK.OsmImporter.Core.Import;

/// <summary>
/// Turns a parsed OSM document into CoK map content and merges it into a base
/// <see cref="MommapDocument"/> (loaded from --template, or a blank canvas).
/// </summary>
public sealed class OsmToMommapConverter
{
    private readonly MappingConfig _mapping;
    private readonly AssetCatalog.AssetCatalog _catalog;
    private readonly WayClassifier _classifier;

    public OsmToMommapConverter(MappingConfig mapping, AssetCatalog.AssetCatalog catalog)
    {
        _mapping = mapping;
        _catalog = catalog;
        _classifier = new WayClassifier(mapping);
    }

    public ImportSummary Convert(OsmDocument osm, MommapDocument target, ConverterOptions options)
    {
        var bbox = options.Bbox ?? osm.Bounds ?? ComputeExtent(osm);
        var projector = new EquirectangularProjector(bbox.Center, options.MetersPerUnit);
        var simplifyEpsilon = options.SimplificationUnits;

        var ctx = new EmitContext
        {
            Options = options,
            Summary = new ImportSummary { UsedBbox = bbox },
            NewObjects = new List<MapObject>(),
            NewPaths = new List<MapPath>(),
            NextUid = target.Info.LastUid + 1,
            InheritedAreaIdx = target.Paths.Count > 0 ? target.Paths[0].ClickAndPlacingWo.AssignedPathAreaIdx : 0,
        };

        foreach (var way in osm.Ways.Values)
        {
            if (way.Tags.Count == 0)
                continue;
            if (!TryResolveWayPoints(way, osm, projector, bbox, out var points))
                continue;

            ClassifyAndEmitWay(way.Id, way.Tags, way.IsClosed, points, simplifyEpsilon, ctx);
        }

        // Multipolygon relations: only the common "one outer ring, no holes" shape is supported
        // in v1 (see plan). Note this can double-emit a polygon if the outer way happens to carry
        // the same tags as its relation (sloppy but not-unheard-of source data) — acceptable for v1.
        foreach (var relation in osm.Relations.Values)
            ProcessRelation(relation, osm, projector, bbox, simplifyEpsilon, ctx);

        foreach (var node in osm.Nodes.Values)
        {
            if (!node.HasTags)
                continue;
            if (!node.Tags.TryGetValue("natural", out var naturalValue) || naturalValue != "tree")
                continue;

            var geo = new GeoPoint(node.Lat, node.Lon);
            if (!bbox.Contains(geo))
                continue;

            ctx.NewObjects.Add(BuildTreeObject(node.Id, projector.Project(geo)));
            ctx.Summary.Trees++;
        }

        if (options.Merge == MergeMode.Replace)
        {
            target.Objects.Clear();
            target.Paths.Clear();
        }

        target.Objects.AddRange(ctx.NewObjects);
        target.Paths.AddRange(ctx.NewPaths);
        if (ctx.NewPaths.Count > 0)
            target.Info.LastUid = ctx.NextUid - 1;

        return ctx.Summary;
    }

    private sealed class EmitContext
    {
        public required ConverterOptions Options { get; init; }
        public required ImportSummary Summary { get; init; }
        public required List<MapObject> NewObjects { get; init; }
        public required List<MapPath> NewPaths { get; init; }
        public int NextUid { get; set; }
        public int InheritedAreaIdx { get; init; }
    }

    private void ClassifyAndEmitWay(
        long id, IReadOnlyDictionary<string, string> tags, bool isClosed, IReadOnlyList<LocalPoint> points, double simplifyEpsilon, EmitContext ctx)
    {
        var kind = _classifier.Classify(tags, isClosed);

        // Simplification is only for the dense, GPS-noisy geometry CoK's *path* tools choke on —
        // it must never touch buildings: a small footprint (a house is often <1 map unit across
        // at a heavily-compressed --scale) can collapse to 2 points under the same tolerance that's
        // perfectly reasonable for a 100-point road, which zeroes out DominantEdgeAngleDegrees and
        // leaves every affected building facing the same default direction.
        var pathPoints = kind is WayFeatureKind.Building or WayFeatureKind.None
            ? points
            : GeometryHelpers.SimplifyPolyline(points, simplifyEpsilon);

        switch (kind)
        {
            case WayFeatureKind.Waterway:
                // Width in mapping.json is real-world meters, same as everything else — scale it
                // down with --scale just like coordinates, so a compressed map gets proportionally
                // narrower rivers instead of disproportionately wide ones.
                var riverWidth = (_mapping.Waterway.Asset.Width ?? 8.0) / ctx.Options.MetersPerUnit;
                ctx.NewPaths.Add(BuildWaterwayPath(pathPoints, _mapping.Waterway.Asset.Filename, _mapping.Waterway.Asset.PathType, ctx, riverWidth));
                ctx.Summary.Rivers++;
                break;

            case WayFeatureKind.WaterPolygon:
                ctx.NewPaths.Add(BuildPolygonPath(GeometryHelpers.DedupeClosingPoint(pathPoints), _mapping.WaterPolygon.Asset.Filename, _mapping.WaterPolygon.Asset.PathType, ctx, _mapping.WaterPolygon, id));
                ctx.Summary.WaterPolygons++;
                break;

            case WayFeatureKind.ForestPolygon:
                ctx.NewPaths.Add(BuildPolygonPath(GeometryHelpers.DedupeClosingPoint(pathPoints), _mapping.ForestPolygon.Asset.Filename, _mapping.ForestPolygon.Asset.PathType, ctx, _mapping.ForestPolygon, id));
                ctx.Summary.ForestPolygons++;
                break;

            case WayFeatureKind.FarmlandPolygon:
                ctx.NewPaths.Add(BuildPolygonPath(GeometryHelpers.DedupeClosingPoint(pathPoints), _mapping.FarmlandPolygon.Asset.Filename, _mapping.FarmlandPolygon.Asset.PathType, ctx, _mapping.FarmlandPolygon, id));
                ctx.Summary.FarmlandPolygons++;
                break;

            case WayFeatureKind.Barrier:
                var barrierAsset = _mapping.Barriers[tags["barrier"]];
                ctx.NewPaths.Add(BuildLinePath(pathPoints, barrierAsset.Filename, barrierAsset.PathType, ctx));
                ctx.Summary.Barriers++;
                break;

            case WayFeatureKind.Building:
                if (ctx.Options.Profile == ImportProfile.Base)
                {
                    ctx.Summary.BuildingsSkippedByProfile++;
                    break;
                }
                ctx.NewObjects.Add(BuildBuildingObject(id, tags, pathPoints));
                ctx.Summary.Buildings++;
                break;

            case WayFeatureKind.Road:
                var highwayValue = tags.GetValueOrDefault("highway", "default");
                var roadRule = _mapping.Roads.TryGetValue(highwayValue, out var rr) ? rr : _mapping.Roads["default"];
                ctx.NewPaths.Add(BuildLinePath(pathPoints, _mapping.RoadFilename, roadRule.PathType, ctx));
                ctx.Summary.Roads++;
                break;

            case WayFeatureKind.None:
                ctx.Summary.Unmapped.Add(new UnmappedFeature("way", id, TagsSummary(tags), "no matching mapping rule"));
                break;
        }
    }

    private void ProcessRelation(OsmRelation relation, OsmDocument osm, EquirectangularProjector projector, BoundingBox bbox, double simplifyEpsilon, EmitContext ctx)
    {
        if (!relation.Tags.TryGetValue("type", out var relType) || relType != "multipolygon")
            return;
        if (relation.Tags.Count <= 1)
            return; // nothing but the "type" tag — nothing to classify

        var outerMembers = relation.Members
            .Where(m => m.Type == OsmRelationMemberType.Way && m.Role == "outer")
            .ToList();
        if (outerMembers.Count != 1)
        {
            ctx.Summary.SkippedComplexRelations++;
            return;
        }

        if (!osm.Ways.TryGetValue(outerMembers[0].Ref, out var outerWay) || !outerWay.IsClosed)
        {
            ctx.Summary.SkippedComplexRelations++;
            return;
        }

        if (!TryResolveWayPoints(outerWay, osm, projector, bbox, out var points))
            return;

        var simplified = GeometryHelpers.SimplifyPolyline(points, simplifyEpsilon);
        var kind = _classifier.Classify(relation.Tags, isClosed: true);
        var uniquePoints = GeometryHelpers.DedupeClosingPoint(simplified);
        switch (kind)
        {
            case WayFeatureKind.WaterPolygon:
                ctx.NewPaths.Add(BuildPolygonPath(uniquePoints, _mapping.WaterPolygon.Asset.Filename, _mapping.WaterPolygon.Asset.PathType, ctx, _mapping.WaterPolygon, relation.Id));
                ctx.Summary.WaterPolygons++;
                break;
            case WayFeatureKind.ForestPolygon:
                ctx.NewPaths.Add(BuildPolygonPath(uniquePoints, _mapping.ForestPolygon.Asset.Filename, _mapping.ForestPolygon.Asset.PathType, ctx, _mapping.ForestPolygon, relation.Id));
                ctx.Summary.ForestPolygons++;
                break;
            case WayFeatureKind.FarmlandPolygon:
                ctx.NewPaths.Add(BuildPolygonPath(uniquePoints, _mapping.FarmlandPolygon.Asset.Filename, _mapping.FarmlandPolygon.Asset.PathType, ctx, _mapping.FarmlandPolygon, relation.Id));
                ctx.Summary.FarmlandPolygons++;
                break;
            default:
                ctx.Summary.SkippedComplexRelations++;
                break;
        }
    }

    private static bool TryResolveWayPoints(
        OsmWay way, OsmDocument osm, EquirectangularProjector projector, BoundingBox bbox, out List<LocalPoint> points)
    {
        points = new List<LocalPoint>(way.NodeIds.Count);
        var anyInBbox = false;
        foreach (var nodeId in way.NodeIds)
        {
            if (!osm.Nodes.TryGetValue(nodeId, out var node))
            {
                points.Clear();
                return false; // referenced node missing from this extract; can't place the way
            }

            var geo = new GeoPoint(node.Lat, node.Lon);
            if (bbox.Contains(geo))
                anyInBbox = true;
            points.Add(projector.Project(geo));
        }

        return anyInBbox && points.Count >= 2;
    }

    private MapPath BuildLinePath(IReadOnlyList<LocalPoint> points, string filename, int pathType, EmitContext ctx)
    {
        var asset = _catalog.GetPath(filename, pathType);

        var segmentCount = Math.Max(points.Count - 1, 0);
        return new MapPath
        {
            Filename = filename,
            PathType = pathType,
            MainPoints = points.Select(p => new PathPoint(p.X, p.Z)).ToList(),
            PointsObjects = points.Select((p, i) => new PointObject { PositionX = p.X, PositionZ = p.Z, AssignedPathPointIdx = i }).ToList(),
            LinesObjects = BuildLineObjectGroups(points, segmentCount, asset.LineTile),
            LinesCustomScales = Enumerable.Repeat(1.0, segmentCount).ToList(),
            ClickAndPlacingWo = BuildClickAndPlacingWo(points, ctx),
        };
    }

    /// <summary>
    /// Rivers are a different animal from roads: instead of a tile prefab, each straight segment
    /// gets an un-skinned placeholder *rectangle* (scale_x = length, scale_z = water width) plus a
    /// pair of left/right riverbank offset points in <c>lines_border_points</c> — confirmed against
    /// a hand-drawn straight *and* a hand-drawn curved test river in the CoK editor, both with
    /// completely empty <c>lines_objects</c> on every segment. (An earlier attempt also emitted a
    /// placeholder rectangle into <c>lines_objects</c> based on the official template.mommap's own
    /// rivers having one — those turned out to be an older/vestigial format quirk, not required by
    /// the current editor, and adding it ourselves triggered a "path invalid" error in-game.)
    /// </summary>
    private static MapPath BuildWaterwayPath(IReadOnlyList<LocalPoint> points, string filename, int pathType, EmitContext ctx, double width)
    {
        var segmentCount = Math.Max(points.Count - 1, 0);
        var borderPoints = new List<LineBorderPointSet>(segmentCount);
        var halfWidth = width / 2.0;

        for (var i = 0; i < segmentCount; i++)
        {
            var a = points[i];
            var b = points[i + 1];

            var dx = b.X - a.X;
            var dz = b.Z - a.Z;
            var len = Math.Sqrt(dx * dx + dz * dz);
            len = len > 1e-9 ? len : 1.0;
            var perpX = -dz / len * halfWidth;
            var perpZ = dx / len * halfWidth;
            borderPoints.Add(new LineBorderPointSet
            {
                PointsLeft = { new PathPoint(a.X + perpX, a.Z + perpZ), new PathPoint(b.X + perpX, b.Z + perpZ) },
                PointsRight = { new PathPoint(a.X - perpX, a.Z - perpZ), new PathPoint(b.X - perpX, b.Z - perpZ) },
            });
        }

        return new MapPath
        {
            Filename = filename,
            PathType = pathType,
            MainPoints = points.Select(p => new PathPoint(p.X, p.Z)).ToList(),
            PointsObjects = points.Select((p, i) => new PointObject
            {
                PositionX = p.X,
                PositionZ = p.Z,
                AssignedPathPointIdx = i,
                ScaleCustomX = width,
            }).ToList(),
            LinesObjects = BuildLineObjectGroups(points, segmentCount, lineTile: null),
            LinesCustomScales = Enumerable.Repeat(1.0, segmentCount).ToList(),
            LinesBorderPoints = borderPoints,
            ClickAndPlacingWo = BuildClickAndPlacingWo(points, ctx),
        };
    }

    private static (double MidX, double MidZ, double Length, double RotationY) SegmentMetrics(LocalPoint a, LocalPoint b)
    {
        var dx = b.X - a.X;
        var dz = b.Z - a.Z;
        var length = Math.Sqrt(dx * dx + dz * dz);
        var rotation = -(Math.Atan2(dz, dx) * 180.0 / Math.PI);
        return ((a.X + b.X) / 2.0, (a.Z + b.Z) / 2.0, length, rotation);
    }

    /// <summary>
    /// CoK doesn't render most path styles procedurally from the spline alone — roads, stone
    /// walls, palisades and fences are actually drawn by stretching one tile prefab across each
    /// straight segment (position = segment midpoint, rotation = segment heading, scale_x =
    /// segment length). Verified against a hand-drawn road/bridge in the CoK editor. Features
    /// without a <paramref name="lineTile"/> (rivers, forest/water/farmland plots) render fine
    /// with empty segment groups — that part CoK does do procedurally.
    /// </summary>
    private static List<LineObjectGroup> BuildLineObjectGroups(IReadOnlyList<LocalPoint> points, int segmentCount, LineTile? lineTile)
    {
        var groups = new List<LineObjectGroup>(segmentCount);
        for (var i = 0; i < segmentCount; i++)
        {
            var group = new LineObjectGroup();
            if (lineTile is { } tile)
                group.Objects.Add(BuildLineTileObject(points[i], points[i + 1], i, tile));
            groups.Add(group);
        }

        return groups;
    }

    private static MapObject BuildLineTileObject(LocalPoint a, LocalPoint b, int segmentIndex, LineTile tile)
    {
        var (midX, midZ, length, rotation) = SegmentMetrics(a, b);

        return new MapObject
        {
            Filename = tile.Filename,
            PositionX = midX,
            PositionZ = midZ,
            RotationY = rotation,
            ScaleX = length,
            ObjectType = tile.ObjectType,
            AssignedPathLineIdx = segmentIndex,
            CustomDataGSize = length,
            CustomSize = length,
        };
    }

    private MapPath BuildPolygonPath(
        IReadOnlyList<LocalPoint> uniquePoints, string filename, int pathType, EmitContext ctx,
        TagDrivenPathRule? rule = null, long fillSeedId = 0)
    {
        _catalog.GetPath(filename, pathType);

        var closedRing = uniquePoints.Append(uniquePoints[0]).ToList();
        var segmentCount = uniquePoints.Count;
        return new MapPath
        {
            Filename = filename,
            PathType = pathType,
            IsClosed = true,
            MainPoints = closedRing.Select(p => new PathPoint(p.X, p.Z)).ToList(),
            PointsObjects = uniquePoints.Select((p, i) => new PointObject { PositionX = p.X, PositionZ = p.Z, AssignedPathPointIdx = i }).ToList(),
            LinesObjects = Enumerable.Range(0, segmentCount).Select(_ => new LineObjectGroup()).ToList(),
            LinesCustomScales = Enumerable.Repeat(1.0, segmentCount).ToList(),
            // Always present (even empty) on every real Plot in template.mommap — CoK never omits
            // this key for a Plot type, only for line-type paths (roads/rivers/barriers).
            AreaObjects = BuildAreaFillObjects(uniquePoints, rule, fillSeedId, ctx.Options.MaxFillObjectsPerPolygon),
            ClickAndPlacingWo = BuildClickAndPlacingWo(uniquePoints, ctx),
        };
    }

    /// <summary>
    /// Bakes a plot's interior fill (see MapPath.AreaObjects doc comment for why this can't be
    /// left to load-time generation). Empty for plot types with no configured fill (water: its
    /// fill is a shader effect, not discrete objects) or an unclosed/degenerate ring. Capped at
    /// <paramref name="maxCount"/> — see ConverterOptions.MaxFillObjectsPerPolygon for why a real
    /// OSM forest polygon's actual area can't be used uncapped.
    /// </summary>
    private List<MapObject> BuildAreaFillObjects(IReadOnlyList<LocalPoint> ring, TagDrivenPathRule? rule, long seedId, int maxCount)
    {
        if (rule is null || rule.Fill.Count == 0 || rule.FillDensity is not > 0 || ring.Count < 3)
            return new List<MapObject>();

        var rng = WeightedPicker.CreateSeededRandom(seedId, salt: 5);
        var scatterPoints = GeometryHelpers.ScatterPointsInPolygon(ring, rule.FillDensity.Value, rng, maxCount);

        var objects = new List<MapObject>(scatterPoints.Count);
        foreach (var p in scatterPoints)
        {
            var assetFilename = WeightedPicker.PickAsset(rule.Fill, rng);
            var scale = 0.8 + rng.NextDouble() * 0.4;
            objects.Add(new MapObject
            {
                Filename = assetFilename,
                PositionX = p.X,
                PositionZ = p.Z,
                RotationY = rng.NextDouble() * 360.0,
                ScaleX = scale,
                ScaleZ = scale,
                ObjectType = _catalog.GetObjectType(assetFilename),
                AssignedPathAreaIdx = 0,
            });
        }

        return objects;
    }

    private static ClickAndPlacingWo BuildClickAndPlacingWo(IReadOnlyList<LocalPoint> points, EmitContext ctx)
    {
        var centroid = GeometryHelpers.Centroid(points);
        return new ClickAndPlacingWo
        {
            PositionX = centroid.X,
            PositionZ = centroid.Z,
            Uid = ctx.NextUid++,
            AssignedPathAreaIdx = ctx.InheritedAreaIdx,
        };
    }

    private MapObject BuildBuildingObject(long id, IReadOnlyDictionary<string, string> tags, IReadOnlyList<LocalPoint> rawPoints)
    {
        var footprint = GeometryHelpers.DedupeClosingPoint(rawPoints);
        if (footprint.Count < 3)
            footprint = rawPoints;

        var centroid = GeometryHelpers.Centroid(footprint);
        var filename = ResolveBuildingAsset(tags, id);
        var objectType = _catalog.GetObjectType(filename);

        return new MapObject
        {
            Filename = filename,
            PositionX = centroid.X,
            PositionZ = centroid.Z,
            RotationY = GeometryHelpers.DominantEdgeAngleDegrees(footprint),
            ScaleX = WeightedPicker.JitterInRange(id, salt: 2, 0.9, 1.1),
            ScaleZ = WeightedPicker.JitterInRange(id, salt: 2, 0.9, 1.1),
            ObjectType = objectType,
        };
    }

    private string ResolveBuildingAsset(IReadOnlyDictionary<string, string> tags, long id)
    {
        foreach (var rule in _mapping.Buildings.Rules)
        {
            if (tags.TryGetValue(rule.TagKey, out var value) && (rule.TagValue == "*" || rule.TagValue == value))
                return WeightedPicker.PickAsset(rule.Assets, id, salt: 1);
        }

        return WeightedPicker.PickAsset(_mapping.Buildings.Default, id, salt: 1);
    }

    private MapObject BuildTreeObject(long id, LocalPoint position)
    {
        var filename = WeightedPicker.PickAsset(_mapping.Trees, id, salt: 0);
        var objectType = _catalog.GetObjectType(filename);

        return new MapObject
        {
            Filename = filename,
            PositionX = position.X,
            PositionZ = position.Z,
            RotationY = WeightedPicker.JitterInRange(id, salt: 3, 0, 360),
            ScaleX = WeightedPicker.JitterInRange(id, salt: 4, 0.85, 1.25),
            ScaleZ = WeightedPicker.JitterInRange(id, salt: 4, 0.85, 1.25),
            ObjectType = objectType,
        };
    }

    private static BoundingBox ComputeExtent(OsmDocument osm)
    {
        if (osm.Nodes.Count == 0)
            throw new InvalidOperationException("OSM document has no nodes; cannot determine an extent to import.");

        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLon = double.MaxValue, maxLon = double.MinValue;
        foreach (var node in osm.Nodes.Values)
        {
            if (node.Lat < minLat) minLat = node.Lat;
            if (node.Lat > maxLat) maxLat = node.Lat;
            if (node.Lon < minLon) minLon = node.Lon;
            if (node.Lon > maxLon) maxLon = node.Lon;
        }

        return new BoundingBox(minLat, minLon, maxLat, maxLon);
    }

    private static string TagsSummary(IReadOnlyDictionary<string, string> tags) =>
        string.Join(";", tags.Select(kv => $"{kv.Key}={kv.Value}"));
}
