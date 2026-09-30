# Map pipeline (Spike B, §30.5)

Turns real Oxford, Ohio data into the files the game loads from `data/map/`:

| Input | Source | License |
|---|---|---|
| Elevation | USGS 3DEP (best available: 1 m lidar, *OH_Statewide_Phase3_2021*, flown Dec 2020 – Jan 2023) | Public domain |
| Buildings, roads, paths, land use, water, campus & city boundaries | OpenStreetMap via Overpass | © OpenStreetMap contributors, ODbL 1.0 |

The map is a 4 km square (size and tile size from `data/balance.json` → `map`) centred on the point in
`data/map/map_config.json`, projected with a transverse-Mercator centred on campus, so grid north = true north
at the centre and distortion across the map is negligible.

## Setup (once)

```powershell
& "$env:LOCALAPPDATA\Programs\Python\Python313\python.exe" -m venv tools\.venv
```

```powershell
tools\.venv\Scripts\python -m pip install -r tools\requirements.txt
```

## Run

```powershell
tools\.venv\Scripts\python tools\map_pipeline\build_map.py
```

Downloads are cached in `tools/_downloads/` (git-ignored); add `--refresh` to fetch again. Previews
(hillshaded relief, all layers) go to `tools/_work/previews/`. Then check the result:

```powershell
dotnet run --project tools/DataValidator
```

Tests:

```powershell
tools\.venv\Scripts\python -m pytest tools\map_pipeline
```

## Outputs (`data/map/`)

| File | Contents |
|---|---|
| `oxford_map.json` | Metadata: grid, projection, bounds, file formats, class codes, sources & attribution |
| `oxford_heightmap.r16` | 801 × 801 little-endian uint16 heights on **tile corners** every 5 m (north row first). `elevation_m = min + v / 65535 × (max − min)` |
| `oxford_landcover.u8` | 400 × 400 land cover per tile (forest, farmland, residential, water, …) |
| `oxford_ownership.u8` | 400 × 400: 0 private, 1 town (City of Oxford limits), 2 university; +128 = protected (nature reserve) |
| `oxford_paths.u8` | 400 × 400 path/road class (footway … primary, railway); thin paths mark every tile they touch |
| `oxford_buildings.u8` | 400 × 400: 1 where a building covers the tile centre |
| `oxford_features.json` | Vectors in tile coordinates: building outlines (+ name, levels, height tags), roads, waterways, university areas |

Tile coordinates: x east, y **south**, origin at the NW corner; tile (i, j) spans [i, i+1) × [j, j+1). Codes are
listed in `oxford_map.json` → `codes`. Binary files are stored with Git LFS.

## If downloads are blocked: manual steps

**Elevation (USGS):**
1. Open <https://apps.nationalmap.gov/downloader/>.
2. Under *Datasets* tick **Elevation Products (3DEP) → 1 meter DEM** (fallback: **1/3 arc-second DEM**).
3. Under *Extent* choose **Coordinates** and enter the map bounds (from `oxford_map.json` → `lonlat_bounds`):
   north **39.5267**, south **39.4907**, west **-84.7570**, east **-84.7104**.
4. Click **Search Products** and download every GeoTIFF listed. For 1 m data the whole map is inside one tile:
   `USGS_1M_16_x69y438_OH_Statewide_Phase3_2021_B21.tif` (several hundred MB).
5. Run the pipeline with the file(s):
   ```powershell
   tools\.venv\Scripts\python tools\map_pipeline\build_map.py --dem-file C:\path\to\USGS_1M_16_x69y438_OH_Statewide_Phase3_2021_B21.tif
   ```
   Repeat `--dem-file` for several tiles; any CRS works.

**OpenStreetMap:**
1. Print the exact query (it has the map's bounding box filled in):
   ```powershell
   tools\.venv\Scripts\python tools\map_pipeline\build_map.py --print-query
   ```
2. Open <https://overpass-turbo.eu>, paste the query, click **Run** (it takes a minute or two).
3. **Export → Data → raw OSM data** and save the `.json` file.
4. Run the pipeline with it:
   ```powershell
   tools\.venv\Scripts\python tools\map_pipeline\build_map.py --osm-file C:\path\to\export.json
   ```

## Settings

`data/map/map_config.json`: centre, heightmap resolution, download pixel size and margins, Overpass servers
(tried in order, with retries: public servers are often busy), and the widths used to rasterize roads, paths and
streams. The pipeline sends a generic User-Agent with no personal details; set the environment variable
`LH_PIPELINE_CONTACT` if you want to add a contact, as OSM's usage policy suggests.

## Known limitations

- OSM has construction dates for almost no buildings (1 of ~3,100 here), so the historic timeline can't come
  from OSM; see `data/timeline.json` and §30.5 step 3 (hand-traced historic maps).
- The DEM is bare earth (buildings removed), which is what the terrain needs.
- OSM land use has gaps (≈ a quarter of tiles are "open": no land-use tag), which become generic grass.
