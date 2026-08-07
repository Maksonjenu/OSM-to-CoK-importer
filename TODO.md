# Known open issues / ideas

Things that are known-broken or known-unsupported, roughly in priority order. Feel free to turn
any of these into a GitHub issue and/or send a PR.

## Twisted/self-intersecting road geometry on real data (not yet investigated)

Reported after the forest-fill work landed: some roads generated from real OSM data come out
twisted/self-intersecting in-game. Not root-caused yet — leading suspects are `SimplifyPolyline`
interacting badly with sharp real-world turns (a simplified point sequence could theoretically
cross itself even when the original didn't), or the road-tile stretch formula breaking down for
very short/sharp segments. Needs a reference file (ideally the specific problem road, before/after
simplification) the same way the road-tile and river-fill formats were figured out.

## Rivers still misbehave on real data

Confirmed working on hand-drawn test rivers (straight and curved) in the CoK editor: a river
segment needs `scale_custom_x` on every vertex placeholder (width) and `lines_border_points`
(bank offset curves), but an **empty** `lines_objects`. That part is fixed. Still open:

- Rivers imported from real OSM data still render thin/narrow and CoK reports "path invalid" even
  after raising `--simplify`. The reconstructed format is probably still missing something, or the
  simplification tolerance needs to be much more aggressive specifically for rivers (they may be
  more angle-sensitive than roads/walls).
- Default river width (8m, `mapping.json`'s `waterway.asset.width`) may just be too small to read
  as a "river" once compressed by `--scale`. Worth experimenting with much larger defaults, or
  scaling width independently of the general `--scale` factor.

**On this branch (`experiment/rivers-as-water-polygons`):** sidesteps the spline "path invalid"
problem entirely by not using the river path type at all — rivers become water *Plots* (the same
`papw_lake.tscn` renderer lakes use) instead. Two sources of geometry, in priority order:

1. **Real OSM water-body vertices, when available (preferred).** Many rivers are mapped twice in
   OSM: a `waterway=river` centerline for routing, plus a separate `natural=water` closed way or
   multipolygon relation with the actual bank-to-bank shape. Real-world multipolygons for rivers
   are very often split across several "outer" way segments rather than one closed way — e.g. the
   actual "Средняя Невка" (Middle Nevka) in the StoneIslandSPB test data is 9 separate outer ways.
   `OsmToMommapConverter.AssembleRings` joins these end-to-end (matching shared node ids, flipping
   direction as needed) into one or more closed rings, which then go through the normal
   `EmitPolygonFeature` pipeline — real vertex count and shape, not an approximation. Verified
   against StoneIslandSPB: water polygons went from 4 (only simple single-way lakes) to 13, with
   the biggest newly-captured shapes running 9–29 vertices — real river outlines, not 4-point
   rectangles. `FindNamedWaterPolygons` collects the `name` of every such real shape up front so
   the matching `waterway=river` centerline (same `name` tag) is skipped instead of drawing a
   redundant synthetic strip on top of it.
2. **Synthetic buffer, as a fallback.** For a `waterway=river`/`stream` line with no same-named
   real polygon in the data (common for minor streams/drains, which OSM usually only maps as a
   centerline), the centerline is buffered left/right by half its width into a closed ring
   (`GeometryHelpers.BuildBufferPolygon`, mitered joins, flat end caps) — a rough approximation,
   better than nothing.

Either way the result goes through `EmitPolygonFeature`, so it also gets grid-split against
`--max-plot-area` like any other oversized plot. This is the default
(`ConverterOptions.RiversAsWaterPolygons = true`); pass `--rivers-as-splines` to fall back to the
old spline renderer entirely (skips both of the above). **Needs in-game confirmation** the same way
forest-fill did: does a real river now render as a visible, correctly-shaped water body, and does
splitting a large one avoid "area too large"? Unit tested (`GeometryHelpersTests.BuildBufferPolygon_*`,
`ConverterIntegrationTests.Convert_MultiWayOuterRelation_*`,
`Convert_NamedWaterwayMatchingRealPolygon_*`) but not yet eyeballed in the CoK editor. The
name-matching dedup is a heuristic (not geometric containment) — an unnamed river/polygon pair
won't be matched and both get drawn, redundantly but harmlessly. If confirmed, the still-open width
question above still applies to the *fallback* case — a buffered plot only 8m wide may read as a
puddle, not a river.

**Side benefit:** the same multi-outer-way ring assembly applies to forest/farmland relations too,
not just water — on StoneIslandSPB, forest/scrub polygons went from 48 to 51 and skipped "too
complex" relations dropped from 210 to 201, for free.

## Forest plots (RESOLVED on `experiment/forest-area-fill`, needs in-game confirmation)

Root cause found via a user-provided reference file (a forest zone hand-drawn — and separately,
one where the user watched CoK's own tooling auto-fill it — in the real editor): CoK does NOT fill
a Plot's interior procedurally at load time. It bakes actual tree/grass object instances into a
field this importer never modeled, `area_objects`, at draw/edit time in the editor. A plot with an
empty `area_objects` (every earlier version of this importer, and apparently the official
template.mommap's own forest zones too) renders as an empty, invisible zone — nothing to do with
point count or CoK version.

Implemented on `experiment/forest-area-fill`: bake the fill ourselves (weighted scatter of
trees/grass/deadwood at the reference density, capped per polygon — see commit for the "real OSM
forest polygons are way bigger than anything hand-drawn in CoK" practicality problem this ran
into). **Confirmed working in-game** — trees actually render now.

Follow-up bug found once fill was confirmed working: some generated forest polygons trigger an
in-game "area too large" warning/glitch (no exact threshold given by CoK, just a general
warning). Added grid-based polygon splitting (`GeometryHelpers.SplitPolygonIntoGrid`,
`ConverterOptions.MaxPlotAreaUnits`, CLI `--max-plot-area`, default 2000 units² — the largest
confirmed-working reference size) — a big polygon becomes several smaller ones instead of one
oversized one, each within the cap, still covering the full original area. Still needs: in-game
confirmation that splitting actually clears the warning (implemented reactively based on the
report, not yet re-verified), and the visible seam along grid lines is an accepted tradeoff, not
fixed (no attempt at hiding/blending the cut). If it doesn't fully clear the warning, the real
threshold may be lower than 2000 — try lowering `--max-plot-area`.

Farmland (`pap_field.tscn`) uses a completely different fill mechanic — not a scatter, but *rows*
of a stretched `wo_salad_row_path_a.tscn` object (same stretch-tile idea as roads, arranged in
parallel lines across the polygon). Not implemented — farmland plots still import with empty
`area_objects` and won't render. Lower priority than forest was; needs a row-direction + spacing
algorithm, structurally more work than the tree scatter.

## Sea/ocean around islands not supported

CoK has a `papw_ocean.tscn` plot type, but there's no way to express "everything outside this
landmass is ocean" from OSM data with the current importer — that needs proper multipolygon
support with **inner rings** (holes), i.e. drawing the ocean as the map's outer bounds with the
island's coastline as a hole. Current relation handling only supports the single-outer-ring,
no-holes case (see `OsmToMommapConverter.ProcessRelation`); islands/lakes-with-islands just import
as if the hole weren't there.

## Hedges are unsupported

`pa_hedge.tscn` uses a bush-scatter system (many small `wo_bush_*` objects per segment, not a
single stretched tile) that hasn't been reverse engineered. `barrier=hedge` currently maps to
`pa_hedge.tscn` but never gets segment content, so it won't be visible. Low priority — real-world
OSM data rarely tags much as `barrier=hedge` compared to `fence`/`wall`.

## Roads/buildings mapping is a guess, needs community eyes

`default-mapping.json`'s `roads` section (which `path_type` 17–23 maps to which OSM `highway=`
class) and `buildings` section (which prefab fits which OSM tag) were both filled in without being
able to see the CoK editor directly — see the `notes` section at the top of that file. If you've
actually looked at what each `path_type`/prefab looks like in-game, PRs tightening these up are
very welcome.

## Multipolygon relations with holes generally

Not just oceans — any `natural=water`/`natural=wood`/`landuse=farmland` relation with an inner
ring (a pond with an island, a forest with a clearing cut out) currently imports the outer ring
only, ignoring the hole.

## Building placement fidelity

Buildings are placed at the OSM footprint's centroid with a rough heading (the direction of the
footprint's longest edge) and mild random scale jitter — not a real fit to the source footprint's
actual size/shape. Good enough for "a building exists roughly here, roughly this size," not for
anything more precise.
