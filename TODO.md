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

Farmland (`pap_field.tscn`) uses a completely different fill mechanic from forest — see the
dedicated section below, now implemented on `experiment/elevation`.

## Farmland fence + crop rows (RESOLVED on `experiment/elevation`, needs in-game confirmation)

Root-caused via a second user-provided reference file (`elevation_and_farms.mommap`, a real
hand-drawn field): a farmland Plot needs its boundary edges filled with a stretched
`wo_fence_a.tscn` tile (one per edge — the exact same stretched-tile mechanism roads/barriers
already use, just applied to the Plot's own ring) *and* its interior filled with rows of
`wo_salad_row_path_d.tscn` baked into `area_objects` (object_type 51 — missing from the asset
catalog before this, added). Without both, a farmland plot imports as an empty, unenclosed zone,
same "not generated at load time" issue forest had before its fix.

Row placement is an explicit **approximation**, not a byte-for-byte replica of CoK's own row
generator: the reference field's rows sit at a `rotation_y` of ~32.9° while the Plot's own
`layout_rotation` field says 57.0° (a different angle reference, not simply the same value under
another name — see `MapPath.LayoutRotation`'s doc comment), and some rows are split into 2-3
collinear pieces for reasons not fully understood (possibly a max single-mesh-length cap around
~11.4 units — not replicated). `GeometryHelpers.GenerateParallelRows` instead picks a seeded-random
angle and lays out evenly-spaced rows clipped to the polygon boundary (works on non-convex shapes),
each clipped span becoming exactly one object regardless of length. Reuses
`ConverterOptions.MaxFillObjectsPerPolygon`/`--max-fill` as a flat per-piece cap so a large real
farmland polygon can't run away the object count, same concern forest fill had. **Needs in-game
confirmation** — verified only against the unit tests and a real-data smoke run (Cantia_Italy.osm:
44 fields, 1614 total row objects, no field left with zero rows), not eyeballed in the CoK editor.

## Elevation / terrain (EXPERIMENTAL, new on `experiment/elevation`)

CoK has no smooth heightmap to import real terrain into — confirmed against the same
`elevation_and_farms.mommap` reference file: elevation is entirely independent flat *Plateau*
plots (`papl_plateau_plateau.tscn`, path_type 68), each a closed polygon with its own
`height_changing_position_y` (values seen: 2 up to a ~30 cap, absent = flat/height 0). There is no
concept of one continuous sloped surface — a "hill" in CoK is a stack/grid of separately-drawn flat
plateaus.

Implemented: `OsmToMommapConverter.AddElevationPlateausAsync` tiles the map's bbox into a grid of
square cells (`--elevation-grid <units>`, output map units, not scaled by `--scale`), samples
real-world elevation at each cell's center via the free [Open-Meteo Elevation
API](https://open-meteo.com/en/docs/elevation-api) (Copernicus DEM GLO-90, ~90m resolution, no API
key needed, batched requests — chosen over paid alternatives like TessaDEM for a first pass with no
signup friction), and emits one Plateau per cell raised to `(elevation - bboxMinElevation) /
--elevation-scale` (default 5 real meters per height unit, independent of the horizontal `--scale`
since a plateau's height cap of ~30 units has nothing to do with map compression). Cells within
~0.05 units of the bbox's lowest point are skipped (a height-0 plateau changes nothing visible).
Opt-in only (omit `--elevation-grid` to skip entirely) since it's the only network-dependent step
in the whole pipeline; a failed lookup (network down, API changed) logs a warning and the rest of
the map still saves without terrain.

**Known limitations / not attempted:**
- The result is deliberately a blocky/terraced grid, not a smooth slope — matches how CoK's own
  Plateau system works, but a real hillside will look like a wargaming-terrain staircase rather
  than a gradient. No attempt at blending/smoothing between adjacent cells of different heights.
- Open-Meteo's ~90m resolution is coarse for small maps — a `--elevation-grid` smaller than that
  will sample the same underlying DEM pixel for multiple adjacent cells, producing visually
  identical/stepped-together plateaus where finer real variation exists. TessaDEM (paid,
  ~€0.001/request) or a downloaded SRTM/Copernicus GLO-30 tile sampled offline would both give
  finer resolution — worth revisiting if the coarse grid doesn't look good in-game.
- No in-game confirmation yet that a grid of many adjacent/abutting Plateau plots actually looks
  reasonable (vs. e.g. z-fighting, gaps, or CoK enforcing some minimum spacing between plots) — only
  verified via unit tests (fake elevation provider, no network) and one real end-to-end run against
  the live Open-Meteo API (Cantia_Italy.osm, `--elevation-grid 50`: 13 plateaus, heights 1.2-16.2,
  plausible for that hilly region).
- Doesn't affect existing roads/buildings/water at all — everything else still sits at `y = 0`
  regardless of the terrain grid drawn under it, so a road crossing a raised plateau will currently
  clip through/float above it rather than following the new terrain. Not attempted in this pass.

## Sea/ocean around islands not supported

CoK has a `papw_ocean.tscn` plot type, but there's no way to express "everything outside this
landmass is ocean" from OSM data with the current importer — that needs proper multipolygon
support with **inner rings** (holes), i.e. drawing the ocean as the map's outer bounds with the
island's coastline as a hole. `OsmToMommapConverter.ProcessRelation`/`AssembleRings` now join
multiple **outer** way segments into a ring (see the rivers-as-water-polygons fix), but inner-role
members are still ignored outright — islands/lakes-with-islands import as if the hole weren't
there. Real-world scale of the problem, found via `grecia.osm`: the Aegean Sea itself is mapped as
a single `place=sea` multipolygon relation with 2055 outer + 1768 inner way members covering the
whole region — even with holes supported, a relation that size is its own scaling problem
(`AssembleRings`' ring-joining is O(n²) per relation) and `place=sea` isn't even a tag this importer
currently recognizes as water at all (`water_polygon.natural_values` only has `water`/`wetland`).

## Hedges are unsupported

`pa_hedge.tscn` uses a bush-scatter system (many small `wo_bush_*` objects per segment, not a
single stretched tile) that hasn't been reverse engineered. `barrier=hedge` currently maps to
`pa_hedge.tscn` but never gets segment content, so it won't be visible. Low priority — real-world
OSM data rarely tags much as `barrier=hedge` compared to `fence`/`wall`.

## Roads/buildings/bridge mapping is a guess, needs community eyes

`default-mapping.json`'s `roads` section (which `path_type` 17–23 maps to which OSM `highway=`
class) and `buildings` section (which prefab fits which OSM tag) were both filled in without being
able to see the CoK editor directly — see the `notes` section at the top of that file. If you've
actually looked at what each `path_type`/prefab looks like in-game, PRs tightening these up are
very welcome.

**Bridges (on `experiment/rivers-as-water-polygons`, RESOLVED but guessed asset):** a way tagged
`highway=*` + a truthy `bridge` value (`yes`/`viaduct`/`aqueduct`/... — anything but absent/`no`)
was rendering as a plain road; the `bridge` tag was never checked at all, so `pa_road.tscn` was
used even where a bridge belongs. Fixed by adding a `bridge` entry to `MappingConfig` (a way tagged
as a bridge now uses `pa_bridge_1.tscn`, path_type 34, instead of the road prefab — CoK's bridge
meshes are a different filename entirely, not just another road path_type). Verified on
StoneIslandSPB: 63 of 72 `bridge=*`-tagged ways in the source data now correctly resolve to the
bridge asset (the gap is bridge tags on non-`highway` ways, e.g. railways, which this importer
doesn't classify as roads at all — separate, lower-priority scope). Which of the two bridge prefabs
(`pa_bridge_1.tscn` path_type 34, or `pa_bridge_2.tscn` path_type 35) actually looks right, and
whether it's worth splitting by road class/width, still needs eyes in the actual editor — same
"guessed, needs community eyes" caveat as roads/buildings. No elevation/deck-height modeling is
attempted (see "real terrain/elevation" limitation below) — this only fixes which prefab gets used.

## Multipolygon relations with holes generally

Not just oceans — any `natural=water`/`natural=wood`/`landuse=farmland` relation with an inner
ring (a pond with an island, a forest with a clearing cut out) currently imports the outer ring
only, ignoring the hole. Worse for anything NOT water/forest/farmland: `ProcessRelation` only
processes relations that classify as one of those three kinds at all, so a `building=yes`
multipolygon (a real example from `grecia.osm`: a building with an inner courtyard, 1 outer + 1
inner way) isn't imported even outer-ring-only — it's dropped completely, counted as a "skipped
complex relation". Building multipolygons aren't common in OSM data but do exist for larger/complex
footprints (courtyards, connected building complexes).

## More unmapped tag categories (found via grecia.osm — candidates for mapping.json)

Real-world OSM data uses plenty of tags this importer has no rule for at all (not a bug, just
uncovered ground — a `--unmapped-report` CSV always shows these; use it on your own data to find
more). From `grecia.osm` (68 unmapped combinations total), the most common by frequency:

- `natural=bare_rock` (16×), `natural=beach`/`natural=sand` (2× each), `natural=cliff` — bare
  terrain types with no CoK equivalent picked yet.
- `natural=coastline` (11×) — an OSM line-only marker for where land meets sea, not an area; almost
  certainly should stay unmapped rather than get a fill rule (there's no "land" polygon to draw).
- `landuse=grass` (7×), `leisure=garden` (6×) — plain groundcover, no plot type assigned.
- `leisure=pitch;sport=soccer`/`sport=basketball` (3×), `leisure=marina` (a `type=multipolygon`
  relation, separately counted as a skipped relation above), `landuse=cemetery`, `place=square` —
  more specific land uses with no obvious CoK prefab match.
- `area=yes;man_made=pier` (4×), `area=yes;man_made=breakwater` (2×) — waterfront structures.
- `barrier=retaining_wall` (2×) — a barrier subtype not in the `barriers` mapping (currently only
  hedge/wall/city_wall/fence).
- `route=ferry` relations (3×), `aeroway=helipad` (1×) — transit infrastructure with no path/point
  equivalent at all; probably out of scope for a tabletop-RPG-map importer rather than something to
  add.

## Building placement fidelity

Buildings are placed at the OSM footprint's centroid with a rough heading (the direction of the
footprint's longest edge) and mild random scale jitter — not a real fit to the source footprint's
actual size/shape. Good enough for "a building exists roughly here, roughly this size," not for
anything more precise.
