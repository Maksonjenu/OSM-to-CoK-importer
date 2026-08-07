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
            NamedWaterPolygonNames = FindNamedWaterPolygons(osm),
        };

        foreach (var way in osm.Ways.Values)
        {
            if (way.Tags.Count == 0)
                continue;
            if (!TryResolveWayPoints(way.NodeIds, osm, projector, bbox, out var points))
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
        public required IReadOnlySet<string> NamedWaterPolygonNames { get; init; }
    }

    /// <summary>
    /// Real rivers are very often mapped twice in OSM: a <c>waterway=river</c> centerline (for
    /// routing/network purposes) plus a separate <c>natural=water</c> closed way or multipolygon
    /// relation carrying the actual bank-to-bank shape, sharing the same `name`. Once that real
    /// shape is emitted (see ProcessRelation/ClassifyAndEmitWay's WaterPolygon case), buffering the
    /// centerline into a second, synthetic water plot on top of it would just double-draw the same
    /// river. This collects the `name` of every water-polygon-shaped feature up front so the
    /// centerline case can skip itself when a same-named real shape exists. Matching by name is a
    /// heuristic (not a geometric containment check) but real-world river tagging is consistent
    /// enough for this to work in practice; an unnamed river/polygon pair just won't be matched and
    /// falls back to drawing both, same as before.
    /// </summary>
    private HashSet<string> FindNamedWaterPolygons(OsmDocument osm)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var way in osm.Ways.Values)
        {
            if (way.IsClosed && _classifier.Classify(way.Tags, isClosed: true) == WayFeatureKind.WaterPolygon
                && way.Tags.TryGetValue("name", out var wayName) && !string.IsNullOrWhiteSpace(wayName))
                names.Add(wayName);
        }

        foreach (var relation in osm.Relations.Values)
        {
            if (relation.Tags.TryGetValue("type", out var relType) && relType == "multipolygon"
                && _classifier.Classify(relation.Tags, isClosed: true) == WayFeatureKind.WaterPolygon
                && relation.Tags.TryGetValue("name", out var relName) && !string.IsNullOrWhiteSpace(relName))
                names.Add(relName);
        }

        return names;
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
                var hasRealWaterShape = tags.TryGetValue("name", out var riverName)
                    && ctx.NamedWaterPolygonNames.Contains(riverName);
                if (ctx.Options.RiversAsWaterPolygons && !hasRealWaterShape)
                {
                    // Buffer the centerline into a closed ring and emit it through the same Plot
                    // pipeline as lakes/forests — including grid-splitting for rivers long enough to
                    // exceed MaxPlotAreaUnits (a long river's buffered ring can easily be larger than
                    // any hand-drawn lake). Water has no configured Fill/FillDensity in mapping.json,
                    // so this never bakes area_objects — its "fill" stays the plot's own shader/mesh
                    // water effect, same as a real lake. Only a fallback for rivers with no real OSM
                    // water-body shape (see FindNamedWaterPolygons) — a rough approximation from the
                    // centerline + a configured width, used only when nothing better is available.
                    var ring = GeometryHelpers.BuildBufferPolygon(pathPoints, riverWidth);
                    EmitPolygonFeature(ring, _mapping.WaterPolygon, ctx, id);
                }
                else if (!ctx.Options.RiversAsWaterPolygons)
                {
                    ctx.NewPaths.Add(BuildWaterwayPath(pathPoints, _mapping.Waterway.Asset.Filename, _mapping.Waterway.Asset.PathType, ctx, riverWidth));
                }
                // else: a same-named real water-body polygon already covers this river (emitted
                // from its own natural=water way/relation) — drawing a synthetic buffer on top of
                // it would just double the same water plot, so skip.
                ctx.Summary.Rivers++;
                break;

            case WayFeatureKind.WaterPolygon:
                ctx.Summary.WaterPolygons += EmitPolygonFeature(GeometryHelpers.DedupeClosingPoint(pathPoints), _mapping.WaterPolygon, ctx, id);
                break;

            case WayFeatureKind.ForestPolygon:
                ctx.Summary.ForestPolygons += EmitPolygonFeature(GeometryHelpers.DedupeClosingPoint(pathPoints), _mapping.ForestPolygon, ctx, id);
                break;

            case WayFeatureKind.FarmlandPolygon:
                ctx.Summary.FarmlandPolygons += EmitPolygonFeature(GeometryHelpers.DedupeClosingPoint(pathPoints), _mapping.FarmlandPolygon, ctx, id);
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

        var kind = _classifier.Classify(relation.Tags, isClosed: true);
        if (kind is not (WayFeatureKind.WaterPolygon or WayFeatureKind.ForestPolygon or WayFeatureKind.FarmlandPolygon))
        {
            ctx.Summary.SkippedComplexRelations++;
            return;
        }

        // Real-world water/forest/farmland areas are very often split across several "outer" way
        // segments instead of one closed way (e.g. a river's bank line edited by many people over
        // years) — a real example: "Средняя Невка" (Middle Nevka) is 9 separate outer ways that
        // only form a ring once joined end-to-end. Inner rings (holes/islands) are still not
        // supported — see TODO.md.
        var outerWays = new List<OsmWay>();
        foreach (var member in relation.Members)
        {
            if (member.Type != OsmRelationMemberType.Way || member.Role != "outer")
                continue;
            if (!osm.Ways.TryGetValue(member.Ref, out var way))
            {
                ctx.Summary.SkippedComplexRelations++;
                return; // referenced way missing from this extract; can't reliably assemble a ring
            }
            outerWays.Add(way);
        }
        if (outerWays.Count == 0)
        {
            ctx.Summary.SkippedComplexRelations++;
            return;
        }

        var rings = AssembleRings(outerWays);
        if (rings.Count == 0)
        {
            ctx.Summary.SkippedComplexRelations++;
            return;
        }

        for (var i = 0; i < rings.Count; i++)
        {
            if (!TryResolveWayPoints(rings[i], osm, projector, bbox, out var points))
                continue;

            var simplified = GeometryHelpers.SimplifyPolyline(points, simplifyEpsilon);
            var uniquePoints = GeometryHelpers.DedupeClosingPoint(simplified);
            var seedId = relation.Id + i; // vary the fill seed per ring, same idea as split pieces
            switch (kind)
            {
                case WayFeatureKind.WaterPolygon:
                    ctx.Summary.WaterPolygons += EmitPolygonFeature(uniquePoints, _mapping.WaterPolygon, ctx, seedId);
                    break;
                case WayFeatureKind.ForestPolygon:
                    ctx.Summary.ForestPolygons += EmitPolygonFeature(uniquePoints, _mapping.ForestPolygon, ctx, seedId);
                    break;
                case WayFeatureKind.FarmlandPolygon:
                    ctx.Summary.FarmlandPolygons += EmitPolygonFeature(uniquePoints, _mapping.FarmlandPolygon, ctx, seedId);
                    break;
            }
        }
    }

    /// <summary>
    /// Joins a multipolygon relation's "outer" way segments end-to-end into one or more closed
    /// rings, by matching shared OSM node ids at each way's endpoints (flipping a segment's
    /// direction when it only matches reversed). A way that's already closed on its own (e.g. a
    /// separate island-like outer piece) becomes its own ring immediately. A segment that can't be
    /// connected to close its ring (missing/incomplete source data) is dropped rather than emitted
    /// as a bogus open shape — same "best effort" spirit as the rest of v1's relation handling.
    /// </summary>
    private static List<List<long>> AssembleRings(IReadOnlyList<OsmWay> outerWays)
    {
        var remaining = outerWays.Select(w => w.NodeIds.ToList()).ToList();
        var rings = new List<List<long>>();

        while (remaining.Count > 0)
        {
            var current = remaining[0];
            remaining.RemoveAt(0);

            bool progress;
            do
            {
                progress = false;
                if (current[0] == current[^1])
                    break; // closed already

                for (var i = 0; i < remaining.Count; i++)
                {
                    var seg = remaining[i];
                    if (seg[0] == current[^1])
                        current.AddRange(seg.Skip(1));
                    else if (seg[^1] == current[^1])
                        current.AddRange(((IEnumerable<long>)seg).Reverse().Skip(1));
                    else if (seg[^1] == current[0])
                        current.InsertRange(0, seg.Take(seg.Count - 1));
                    else if (seg[0] == current[0])
                        current.InsertRange(0, ((IEnumerable<long>)seg).Reverse().Take(seg.Count - 1));
                    else
                        continue;

                    remaining.RemoveAt(i);
                    progress = true;
                    break;
                }
            } while (progress);

            if (current.Count >= 4 && current[0] == current[^1])
                rings.Add(current);
        }

        return rings;
    }

    private static bool TryResolveWayPoints(
        IReadOnlyList<long> nodeIds, OsmDocument osm, EquirectangularProjector projector, BoundingBox bbox, out List<LocalPoint> points)
    {
        points = new List<LocalPoint>(nodeIds.Count);
        var anyInBbox = false;
        foreach (var nodeId in nodeIds)
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

    /// <summary>
    /// Emits one or more Plot paths for a polygon feature, splitting it first if it's larger than
    /// ConverterOptions.MaxPlotAreaUnits (see that doc comment — real OSM land-use polygons can be
    /// far larger than anything CoK's own area limit, whatever it actually is, was designed for).
    /// Returns how many paths were added, for the caller's summary count.
    /// </summary>
    private int EmitPolygonFeature(IReadOnlyList<LocalPoint> uniquePoints, TagDrivenPathRule rule, EmitContext ctx, long seedId)
    {
        var pieces = GeometryHelpers.SplitPolygonIntoGrid(uniquePoints, ctx.Options.MaxPlotAreaUnits);

        // The fill budget (see MaxFillObjectsPerPolygon) is for the WHOLE original feature, not
        // per piece — splitting a big polygon into N smaller ones must not let it claim N times
        // the fill of an unsplit polygon the same total size. Computed once against the original,
        // unsplit area and shared out proportionally by each piece's share of that area.
        var originalArea = Math.Abs(GeometryHelpers.SignedArea(uniquePoints));
        var totalFillBudget = rule.FillDensity is > 0
            ? Math.Min(originalArea * rule.FillDensity.Value, ctx.Options.MaxFillObjectsPerPolygon)
            : 0;

        var emitted = 0;
        foreach (var piece in pieces)
        {
            if (piece.Count < 3)
                continue;

            var pieceArea = Math.Abs(GeometryHelpers.SignedArea(piece));
            var pieceFillBudget = originalArea > 1e-9 ? (int)Math.Round(totalFillBudget * (pieceArea / originalArea)) : 0;

            // Vary the fill seed per piece so split polygons don't all scatter identically.
            ctx.NewPaths.Add(BuildPolygonPath(piece, rule.Asset.Filename, rule.Asset.PathType, ctx, rule, seedId + emitted, pieceFillBudget));
            emitted++;
        }
        return emitted;
    }

    private MapPath BuildPolygonPath(
        IReadOnlyList<LocalPoint> uniquePoints, string filename, int pathType, EmitContext ctx,
        TagDrivenPathRule? rule = null, long fillSeedId = 0, int? maxFillObjects = null)
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
            AreaObjects = BuildAreaFillObjects(uniquePoints, rule, fillSeedId, maxFillObjects ?? ctx.Options.MaxFillObjectsPerPolygon),
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
