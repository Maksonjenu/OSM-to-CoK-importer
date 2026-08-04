# CoK OSM Importer

Imports [OpenStreetMap](https://www.openstreetmap.org) data (roads, rivers, lakes, forests,
farmland, trees, buildings) into a [Canvas of Kings](https://store.steampowered.com/) `.mommap`
map file. Built for turning a real place into a starting point for a tabletop-RPG map, not for
perfect cartographic accuracy.

Русская версия: [README.ru.md](README.ru.md)

## What it does and doesn't do

**Imports:** roads (as CoK's road paths), rivers/streams (water splines), lakes/ponds (water
plots), forest/scrub (forest plots — CoK fills these with trees procedurally, we don't place them
individually), farmland/orchards/vineyards ("посадки", plot type), fences/walls/palisades,
point-mapped trees, and buildings (very roughly — see caveats below).

**Does not (yet):** hedges (CoK uses a bush-scatter system for these that hasn't been reverse
engineered), holes in multipolygon relations (lakes/forests with an island get imported as if the
island weren't there — so no "ocean around an island" either), route relations, real
elevation/terrain data (everything is placed at `y = 0`; use CoK's own terrain tools afterward if
you want relief). See [TODO.md](TODO.md) for the full list of known-open problems and ideas —
happy to take issues/PRs against any of it.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later.
- An OSM XML export of the area you want (from [openstreetmap.org](https://www.openstreetmap.org)'s
  "Export" tab, [Overpass Turbo](https://overpass-turbo.eu/), or similar — `.osm`/`.xml`, not
  `.pbf`).
- Optionally, a `.mommap` file to use as a base template (for its lighting/environment settings —
  see [Building on a blank canvas](#building-on-a-blank-canvas) if you don't have one). Any real
  CoK map works — your own save, or one downloaded from the in-game Workshop browser.

No `.osm`/`.mommap` files are bundled with this repo (`.gitignore`'d on purpose — they're either
large personal data or someone else's map content, not this project's to redistribute). Bring your
own.

## Build

```
dotnet build
```

## Run the tests

```
dotnet test
```

(A few tests optionally load `template.mommap`/`mejka_map.osm` at the repo root if you've dropped
your own copies there — they no-op if those files aren't present, so a fresh clone passes the full
suite with zero setup.)

## Quick start

```
dotnet run --project src/CoK.OsmImporter.Cli -- ^
  --osm my-city.osm ^
  --template template.mommap ^
  --center 53.690,88.063 --radius 300 ^
  --scale 10 ^
  --out my-map.mommap
```

(`^` is the Windows `cmd` line-continuation character; use `` ` `` in PowerShell or `\` in
bash/Git Bash.)

This reads `my-city.osm`, keeps `template.mommap`'s lighting/paper settings, imports only a 300m
radius around the given lat/lon, compresses real-world meters 10:1 into map units, and writes
`my-map.mommap`. Open that file in Canvas of Kings.

## More examples

```
# Whole file, no bbox — fine for an already-small area (an island, a village), skip for a whole city.
dotnet run --project src/CoK.OsmImporter.Cli -- --osm island.osm --scale 10 --out island.mommap

# Landscape only — roads/water/forest/trees, no buildings.
dotnet run --project src/CoK.OsmImporter.Cli -- --osm my-city.osm --scale 10 --profile base --out landscape-only.mommap

# Add to a map you've already been editing by hand in CoK, instead of starting fresh.
dotnet run --project src/CoK.OsmImporter.Cli -- --osm extra-district.osm --template my-in-progress-map.mommap --merge append --scale 10 --out my-in-progress-map.mommap

# Still getting "path invalid" or "area too large"? Tighten both.
dotnet run --project src/CoK.OsmImporter.Cli -- --osm my-city.osm --scale 10 --simplify 5 --max-plot-area 1000 --out my-map.mommap

# See what tag combinations got skipped, to extend mapping.json.
dotnet run --project src/CoK.OsmImporter.Cli -- --osm my-city.osm --scale 10 --unmapped-report unmapped.csv --out my-map.mommap
```

## CLI reference

```
cok-osm-import --osm <file.osm> --out <file.mommap> [options]
```

| Flag | Required | Default | Meaning |
|---|---|---|---|
| `--osm <path>` | yes | — | Source OSM XML export. |
| `--out <path>` | yes | — | Output `.mommap` path. |
| `--template <path.mommap>` | no | blank canvas | Base map to build on/into — see below. |
| `--bbox <minLat,minLon,maxLat,maxLon>` | no | whole file | Explicit lat/lon box to import. |
| `--center <lat,lon>` + `--radius <meters>` | no | whole file | Import a circle (as a bbox) around a point. Mutually exclusive with `--bbox`. |
| `--profile <full\|base>` | no | `full` | `full` = roads+water+forest/farmland+trees+buildings. `base` = same minus buildings. |
| `--scale <metersPerUnit>` | no | `1.0` | Real-world meters represented by one CoK map unit. `--scale 10` shrinks everything 10:1. |
| `--simplify <units>` | no | `2.0` | Line-simplification tolerance, in **output map units** (not affected by `--scale`). Raise this if CoK reports "path invalid". `0` disables it. |
| `--max-fill <count>` | no | `2000` | Cap on baked interior-fill objects (trees, grass...) per forest/farmland plot — see [Forest fill](#forest-fill). `0` disables area fill entirely. |
| `--max-plot-area <units²>` | no | `2000` | Splits a Plot polygon (forest/farmland/water) bigger than this into a grid of smaller pieces instead of emitting it whole — see [Forest fill](#forest-fill). `0` or negative disables splitting. |
| `--merge <append\|replace>` | no | `append` | `append` adds imported content to `--template`'s existing objects/paths. `replace` wipes them first (keeps environment/lighting/paper settings). |
| `--mapping <path>` | no | built-in | Use a custom tag→prefab mapping config instead of the shipped default — see [Tuning the mapping](#tuning-the-mapping). |
| `--init-mapping <path>` | no | — | Write the built-in default mapping config to `<path>` and exit (doesn't require `--osm`/`--out`). Edit it, then pass it back via `--mapping`. |
| `--unmapped-report <path>` | no | — | Write a CSV of every tag combination that had no mapping rule, so you can see what got skipped. |
| `--help` | no | — | Show usage. |

### Area selection

Omitting both `--bbox` and `--center`/`--radius` imports the *entire* OSM file's extent. For
anything bigger than a small town this produces an enormous map — the CLI prints a warning when
you do this. In practice you almost always want `--center`/`--radius` (a few hundred meters is
closer to what a hand-made tabletop map actually represents) or a hand-picked `--bbox`.

### Building on a blank canvas

If you don't pass `--template`, the tool starts from a blank canvas — but with *real* default
lighting/fog/water-color/paper settings (copied from a working CoK map at build time), not empty
ones. An earlier version of this tool shipped genuinely empty settings there, which CoK renders as
a fully unlit, transparent, gray screen — if you're hitting that, you have an old build; rebuild
from source.

### Tuning the mapping

`mapping.json` is where all the "OSM tag X → CoK prefab Y" decisions live, as plain editable JSON
— nothing is hardcoded in the importer's source. Get a copy with:

```
dotnet run --project src/CoK.OsmImporter.Cli -- --init-mapping my-mapping.json
```

then edit it and pass it back with `--mapping my-mapping.json`. The file has a `notes` section at
the top flagging the parts that are **educated guesses**, specifically:

- **Roads** (`roads` section): `pa_road.tscn` has 7 visual variants in CoK (`path_type`
  17–23) and there is no way to know from the data alone which one looks like a cobblestone
  street vs. a dirt track. The default groups OSM's `highway=*` hierarchy onto them by a
  reasonable guess. Open CoK, draw one segment of each `path_type`, see what they actually look
  like, and re-map the `highway` values under `"roads"` accordingly.
- **Buildings** (`buildings` section): CoK has no prefab for most real-world building/shop
  categories. The default falls back to generic hut cottages for everything except a handful of
  tag values (`amenity=pub/bar/restaurant/cafe` → tavern, `shop=*`/`amenity=marketplace` → market
  stalls). Add more `rules` entries (evaluated top-to-bottom, first match wins) as you find CoK
  prefabs that fit better.

Everything else in the file (which `natural`/`landuse`/`waterway` values count as forest, water,
farmland, which prefab a river/wall/fence/tree uses) is confirmed against real CoK data — those
values don't usually need touching, though you can still add more values to the lists (e.g. more
`landuse` synonyms for farmland) if your OSM data uses tags the default list doesn't cover yet.
Run with `--unmapped-report unmapped.csv` to see exactly which tag combinations weren't matched by
anything, so you know what's worth adding.

### Forest fill

CoK does **not** fill a forest plot with trees procedurally when the map loads — it bakes actual
tree/grass object instances into the file at draw time in the editor. A plot with no baked fill
renders as an empty, invisible zone, so this importer bakes its own fill for `forest_polygon`
matches (`natural=wood`/`natural=scrub`/`landuse=forest`): a weighted scatter of trees, grass
tufts, and the odd bit of deadwood, at a density/mix reverse-engineered from a real CoK-generated
reference zone. Tunable via `mapping.json`'s `forest_polygon.fill` (the weighted asset list) and
`forest_polygon.fill_density` (objects per output map unit², **not** scaled by `--scale` — same
reasoning as `--simplify`, see below).

`--max-fill` caps this per plot: real OSM forest polygons can cover hundreds of hectares, and
applying CoK's own small-hand-drawn-zone density with no cap produces file sizes and object counts
in the hundreds of thousands to millions. If a single plot's polygon is larger than
`--max-plot-area`, it's split into a grid of smaller pieces first (CoK appears to reject or
visually glitch on an oversized plot, "area too large" — the exact threshold isn't known, so this
defaults to the largest size confirmed working against a real reference map); splitting leaves a
visible seam along the grid lines but keeps the fill budget shared across the pieces rather than
multiplying it.

`landuse=farmland`/orchard/etc. (`farmland_polygon`) still import as empty, unfilled plots —
CoK fills farmland with *rows* of a stretched object (closer to how roads work) rather than a
random scatter, and that hasn't been implemented yet. See [TODO.md](TODO.md).

## Known limitations / things to watch for

- **No "ocean around an island."** CoK's ocean plot needs a hole in the middle (the island); the
  importer only supports simple, hole-free polygons right now. See [TODO.md](TODO.md).
- **Very large imports can be genuinely huge relative to what CoK maps are meant to represent.**
  CoK's own example village is roughly 200×200 map units; a real town at 1:1 scale is thousands of
  units across. Use `--center`/`--radius` and/or `--scale` to bring it down to a size that matches
  the kind of map you actually want (walkable village/district, not an entire city at survey
  scale).
- **"path invalid" errors on rivers/roads/walls** almost always mean CoK's path tools are
  rejecting the geometry's point density or sharp angles — raise `--simplify` (try 4–5) and
  re-import.
- **Occasional twisted/self-intersecting road geometry** has been observed on real OSM data —
  not yet root-caused, likely related to simplification interacting with sharp real-world turns.
  See [TODO.md](TODO.md).
- **River rendering is a best-effort reconstruction, still not fully working on real data.** Its
  required fields were reverse engineered from hand-drawn test rivers in the CoK editor (not from
  official documentation) — that part is confirmed correct, but imported-from-OSM rivers still
  render thin and CoK still reports "path invalid" even after raising `--simplify`. Open problem,
  see [TODO.md](TODO.md).
- Buildings are placed at the footprint's centroid with a rough heading (the longest boundary
  edge's direction) and mild random scale jitter — not a precise fit to the original OSM footprint
  size or shape.
- Tree/building prefab choice is deterministic (seeded by the OSM element's id), so re-running the
  same import with the same `mapping.json` always produces the same result.

## Project layout

```
src/CoK.OsmImporter.Core/   class library — OSM parsing, geo projection, the .mommap data model,
                             the asset catalog, the mapping config, and the conversion pipeline
src/CoK.OsmImporter.Cli/    console entry point (this is what you actually run)
tests/                       xUnit test suite, including round-trip fidelity tests against a real
                             template.mommap and integration tests for the conversion pipeline
```

## Contributing

Issues and PRs welcome — [TODO.md](TODO.md) has a prioritized list of known-open problems,
including a few that just need someone who has actually looked closely at the CoK editor (the
`roads`/`buildings` sections of `mapping.json` are educated guesses; see
[Tuning the mapping](#tuning-the-mapping)). If you reverse-engineer another asset type, the most
reliable method so far has been: hand-draw one small example of it in the CoK editor, save, and
diff the raw JSON against what this tool generates for the same feature — that's how the road and
river formats here were actually figured out, not from any documentation.

## License

[MIT](LICENSE). The reverse-engineered `.mommap` format details (`path_type`/`object_type` id
tables, field names, etc.) are just facts about the file format, not copyrighted game content — no
CoK game assets or map files are included in this repo.
