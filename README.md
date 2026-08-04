# CoK OSM Importer

Imports [OpenStreetMap](https://www.openstreetmap.org) data (roads, rivers, lakes, forests, fields/plantations, trees, buildings) into a map file for the [Canvas of Kings](https://store.steampowered.com/app/2498570/Canvas_of_Kings/) editor (`.mommap`). Built to turn a real-world place into the basis for a tabletop RPG map, **not for accurate cartography**.

Russian version: [README.ru.md](README.ru.md)

![OSM-map](StoneIslandSPB_OSM.png)
![CoK-map](StoneIslandSPB_CoK.JPEG)

## What gets imported and what doesn't

**Imported:** roads (as CoK road paths), ~~rivers/streams (water splines)~~, lakes/ponds (water planes), forest/shrubland (forest planes — CoK fills them with trees procedurally on its own), fields/orchards/vineyards ("plantings" plane type), fences/walls/palisades, point trees, and buildings (very roughly).

**Not yet imported:** hedges (CoK has its own bush-scattering system for these that hasn't been figured out), holes in multipolygons (a lake/forest with an island inside it gets imported as if the island weren't there — so a "sea around an island" won't work either), relation routes (route), real terrain/elevation (everything is placed at `y = 0`; use CoK's own terrain tools after import if you need landscape). The full list of known issues and ideas is in [TODO.md](TODO.md); issues/PRs welcome.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or newer.
- An OSM XML export of the area you want ("Export" tab on [openstreetmap.org](https://www.openstreetmap.org), [Overpass Turbo](https://overpass-turbo.eu/), or similar — you need `.osm`/`.xml`, not `.pbf`).
- Optionally, a `.mommap` file to use as a base template (for its lighting/environment settings — see [Importing without a template](#importing-without-a-template) if you don't have one). Any real CoK map works (this can be your own save or something downloaded from the in-game Workshop).

## Build

```
dotnet build
```

## Tests

```
dotnet test
```

> (Some tests optionally load `template.mommap`/`mejka_map.osm` from the repo root if you've placed your own copies there — if the files aren't present, those tests simply no-op, so the full suite passes even on a fresh clone with no setup at all.)

## Quick start

```
dotnet run --project src/CoK.OsmImporter.Cli -- ^
  --osm my-city.osm ^
  --template template.mommap ^
  --center 53.690,88.063 --radius 300 ^
  --scale 10 ^
  --out my-map.mommap
```

This command reads `my-city.osm`, preserves the lighting/paper settings from `template.mommap`, imports only the area within a 300 m radius of the given coordinates, compresses real-world meters into map units at a 10:1 ratio, and writes `my-map.mommap`. Open this file in Canvas of Kings.

## More examples

```
# Whole file, no bbox — fine for an already-small area (an island, a village).
dotnet run --project src/CoK.OsmImporter.Cli -- --osm island.osm --scale 10 --out island.mommap

# Landscape only — roads/water/forest/trees, no buildings.
dotnet run --project src/CoK.OsmImporter.Cli -- --osm my-city.osm --scale 10 --profile base --out landscape-only.mommap

# Add to a map you're already editing by hand in CoK, instead of starting from scratch (experimental).
dotnet run --project src/CoK.OsmImporter.Cli -- --osm extra-district.osm --template my-in-progress-map.mommap --merge append --scale 10 --out my-in-progress-map.mommap

# If you still get "path invalid" or "area too large", try adjusting `simplify` and `max-plot-area`.
dotnet run --project src/CoK.OsmImporter.Cli -- --osm my-city.osm --scale 10 --simplify 5 --max-plot-area 1000 --out my-map.mommap

# See which tag combinations were skipped, so you can extend mapping.json.
dotnet run --project src/CoK.OsmImporter.Cli -- --osm my-city.osm --scale 10 --unmapped-report unmapped.csv --out my-map.mommap
```

## CLI flag reference

```
cok-osm-import --osm <file.osm> --out <file.mommap> [options]
```

| Flag | Required | Default | Meaning |
|---|---|---|---|
| `--osm <path>` | yes | — | Source OSM XML file. |
| `--out <path>` | yes | — | Output path for the `.mommap`. |
| `--template <path.mommap>` | no | blank canvas | Base map to build on — see below. |
| `--bbox <minLat,minLon,maxLat,maxLon>` | no | whole file | Explicit coordinate rectangle to import. |
| `--center <lat,lon>` + `--radius <meters>` | no | whole file | Import a circle (as a bbox) around a point. Mutually exclusive with `--bbox`. |
| `--profile <full\|base>` | no | `full` | `full` = roads+water+forest/fields+trees+buildings. `base` = the same without buildings. |
| `--scale <metersPerUnit>` | no | `1.0` | How many real-world meters one CoK map unit represents. `--scale 10` compresses everything by a factor of 10 (10 was empirically chosen as the "nicest-looking" value). |
| `--simplify <units>` | no | `2.0` | Line-simplification tolerance, in **output map units** (independent of `--scale`). Increase it if CoK complains "path invalid". `0` disables simplification. |
| `--max-fill <number>` | no | `2000` | Cap on baked fill objects (trees, grass...) per forest/field zone — see [Forest fill](#forest-fill). `0` disables fill entirely. |
| `--max-plot-area <units²>` | no | `2000` | A zone polygon (forest/field/water) larger than this is cut into a grid of smaller pieces instead of staying one whole — see [Forest fill](#forest-fill). `0` or negative disables splitting. |
| `--merge <append\|replace>` | no | `append` | `append` adds the imported content to the existing contents of `--template`. `replace` first clears objects/paths (lighting/paper settings are kept). |
| `--mapping <path>` | no | built-in | Use your own tag→asset mapping config instead of the built-in default — see [Configuring the mapping](#configuring-the-mapping). |
| `--init-mapping <path>` | no | — | Write the built-in default mapping config to `<path>` and exit (doesn't require `--osm`/`--out`). Edit it and pass it back via `--mapping`. |
| `--unmapped-report <path>` | no | — | Write a CSV of all tag combinations for which no rule was found — so you can see what was skipped. |
| `--help` | no | — | Show help. |

### Choosing an area

If neither `--bbox` nor `--center`/`--radius` is given, the **entire** file is imported. For anything bigger than a small village this produces a huge map — the CLI prints a warning in that case. In practice, for testing I used the entire exported `osm` area (the largest available), and with 64 GB of RAM there were no issues.

### Importing without a template

Without `--template`, the tool starts from a blank canvas — but with **real** default lighting/fog/water-color/paper settings (copied from a working CoK map at build time), not empty ones. An earlier version of this tool wrote genuinely empty settings there, which CoK renders as a fully unlit, transparent, gray screen — if that's what you're seeing, your build is outdated; rebuild from source.

### Configuring the mapping

`mapping.json` describes "OSM tag X → CoK prefab Y" style properties as plain, editable JSON — nothing is hardcoded in the importer's code. So you can fix the OSM-tag-to-CoK-tag mappings yourself (PRs welcome, since the tags in this repo were mapped automatically with Claude's help).

Get a copy:

```
dotnet run --project src/CoK.OsmImporter.Cli -- --init-mapping my-mapping.json
```

then edit it and pass it back via `--mapping my-mapping.json`. At the top of the file there's a `notes` section flagging the places where Claude **guessed**, namely:

- **Roads** (the `roads` section): `pa_road.tscn` has 7 visual variants in CoK (`path_type` 17–23). By default, the OSM `highway=*` hierarchy is distributed across them based on Claude's *reasonable* guess (make of that what you will). A to-do item is to open CoK, draw one segment of each `path_type`, see what they actually look like, and re-bind the `highway` values in `"roads"` accordingly.
- **Buildings** (the `buildings` section): CoK doesn't have prefabs for most real-world building/shop categories. Currently everything falls back to plain huts by default, except for a few tag values (`amenity=pub/bar/restaurant/cafe` → tavern, `shop=*`/`amenity=marketplace` → market stalls, though this doesn't feel like it always fires correctly).

### Forest fill

CoK does **not** procedurally fill a forest zone with trees on map load — it bakes actual tree/grass instances into the file at the moment the zone is drawn in the editor. A zone with no baked fill renders as empty, so the importer bakes fill itself for `forest_polygon` matches (`natural=wood`/`natural=scrub`/`landuse=forest`): a weighted scatter of trees, grass tufts, and occasional deadfall, with density/mix derived from a real zone baked by CoK itself as a test. This is configurable via `mapping.json`: `forest_polygon.fill` (a list of assets with weights) and `forest_polygon.fill_density` (objects per output-map unit², **not** dependent on `--scale` — the same logic as `--simplify`, see below).

> I don't know how safe or compatible this is, but no fatal errors have occurred so far at this stage.

`--max-fill` caps this per zone: real OSM forest polygons can be large, while CoK's density was worked out for small, hand-drawn zones. If a single zone's polygon is larger than `--max-plot-area`, it's first cut into a grid of smaller pieces; the split leaves a visible seam along the grid lines, but it divides the fill budget between the pieces rather than multiplying it. That said, some fragments still triggered "area too large" even though there were trees inside the zone.

`landuse=farmland`/orchards/etc. (`farmland_polygon`) are currently imported as empty, unfilled zones — CoK fills fields with *rows* of a stretched object (more like how roads work) rather than a random scatter, and that hasn't been implemented yet. See [TODO.md](TODO.md).

## Known limitations / things to watch out for

- **No "sea around an island."** An ocean plane in CoK needs a hole in the middle (the island), and the importer currently only handles simple polygons without holes. See [TODO.md](TODO.md).
- **Importing a large area can be genuinely huge** relative to what CoK maps are typically meant to be. I tested the importer with the "expanded area" game setting enabled.
- **Occasionally twisted/self-intersecting roads or forest areas** show up with real OSM data; the cause hasn't been found yet, likely related to how line simplification interacts with sharp real-world turns. See [TODO.md](TODO.md).
- **River rendering:** the problem of importing rivers hasn't been solved yet. In OSM data, rivers are represented as a line (with no width); in addition, there's sometimes a "waterbody" polygon in the data, but attempting to draw a CoK "lake" instead of a "river" didn't lead anywhere — the area may need to be cut up the same way forests are.
- Buildings are placed at the centroid of their outline with a rough orientation (based on the outline's longest side) and a small random scale variation; this is not an exact match to the original OSM building's size/shape.
- Tree/building prefab selection is deterministic (based on the OSM element's id), so re-running with the same `mapping.json` always gives the same result.

## Project structure

```
src/CoK.OsmImporter.Core/   class library — OSM parsing, geo-projection, the .mommap data model,
                             the asset catalog, the mapping config, and the conversion pipeline itself
src/CoK.OsmImporter.Cli/    console entry point (this is what actually runs)
tests/                       xUnit test suite, including round-trip tests against a real
                             template.mommap and integration tests for the conversion pipeline
```

## Contributing

Issues and PRs are welcome — [TODO.md](TODO.md) has a prioritized list of known open issues, including ones where you just need to look at the map with certain elements (see [Configuring the mapping](#configuring-the-mapping)). The most reliable approach: draw a small example by hand in the CoK editor, save it, and compare the raw JSON to what this tool generates for the same object — that's exactly how the road and river formats were figured out.

## License

[MIT](LICENSE). Reverse-engineered details of the `.mommap` format (the `path_type`/`object_type` id tables, field names, etc.) are simply facts about the file format, not copyrighted game content. No game assets or CoK map files are included in this repository.