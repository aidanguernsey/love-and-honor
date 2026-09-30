"""Love & Honor offline map pipeline (§30.5): real Oxford, OH terrain + OpenStreetMap layers → data/map/.

Usage (from the repo root, PowerShell):
    tools\\.venv\\Scripts\\python tools\\map_pipeline\\build_map.py
Options:
    --dem-file PATH   use local GeoTIFF(s) instead of downloading (repeatable; any CRS; later files fill gaps)
    --osm-file PATH   use a local Overpass JSON export instead of querying Overpass
    --refresh         ignore cached downloads in tools/_downloads
    --no-previews     skip writing preview PNGs to tools/_work/previews
    --print-query     print the exact Overpass query (for a manual export) and exit

Outputs (data/map/, prefix from map_config.json):
    <prefix>_map.json         metadata: grid, projection, file formats, class codes, sources & attribution
    <prefix>_heightmap.r16    (N+1)² little-endian uint16 heights on tile corners every heightmap_resolution_m
    <prefix>_landcover.u8     tiles² land cover codes          <prefix>_ownership.u8   tiles² ownership (+protected bit)
    <prefix>_paths.u8         tiles² path/road class           <prefix>_buildings.u8   tiles² 1 = building footprint
    <prefix>_features.json    building footprints, roads, waterways, university areas as tile-space vectors
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import pathlib
import sys

import numpy as np
from shapely.geometry import LineString, MultiLineString, MultiPolygon, Polygon, box

sys.path.insert(0, str(pathlib.Path(__file__).parent))

import dem as dem_mod  # noqa: E402
import osm as osm_mod  # noqa: E402
from grid import MAP_DIR, REPO, MapGrid, load_config, load_grid  # noqa: E402
from rasterize import burn_layers, burn_mask, to_local  # noqa: E402

CACHE = REPO / "tools" / "_downloads"
PREVIEWS = REPO / "tools" / "_work" / "previews"


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dem-file", action="append", type=pathlib.Path, default=[])
    ap.add_argument("--osm-file", type=pathlib.Path)
    ap.add_argument("--refresh", action="store_true")
    ap.add_argument("--no-previews", action="store_true")
    ap.add_argument("--print-query", action="store_true")
    args = ap.parse_args(argv)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")

    cfg = load_config()
    grid = load_grid(cfg)
    if args.print_query:
        print(osm_mod.build_query(grid, cfg["osm"]["margin_m"]))
        return 0
    prefix = cfg["output_prefix"]
    print(f"Map: {grid.size_m:.0f} m square centred on {grid.center_lat}, {grid.center_lon}; "
          f"{grid.tiles}x{grid.tiles} tiles of {grid.tile_m} m; heightmap {grid.height_samples}² at {grid.height_res_m} m")

    # ---- Elevation ----
    print("Elevation")
    if args.dem_file:
        dem_files = args.dem_file
        dem_info = {"note": "local files: " + ", ".join(p.name for p in dem_files)}
    else:
        dem_files = [dem_mod.fetch_dem(grid, cfg, CACHE, args.refresh)]
        dem_info = dem_mod.dem_source_info(cfg, grid)
    heights = dem_mod.resample_to_grid(grid, dem_files)
    q, lo, hi = dem_mod.quantize(heights)
    heightmap_file = f"{prefix}_heightmap.r16"
    q.astype("<u2").tofile(MAP_DIR / heightmap_file)
    print(f"  elevation {lo:.2f} … {hi:.2f} m (relief {hi - lo:.1f} m), precision {(hi - lo) / 65535 * 1000:.2f} mm")

    # ---- OpenStreetMap ----
    print("OpenStreetMap")
    osm_path = args.osm_file or osm_mod.fetch_osm(grid, cfg, CACHE, args.refresh)
    raw = json.loads(pathlib.Path(osm_path).read_text(encoding="utf-8"))
    elements = raw.get("elements") or []
    layers = classify(grid, cfg, elements)
    print(f"  {len(elements)} elements → {len(layers['buildings'])} buildings, {len(layers['road_features'])} road/path lines, "
          f"{sum(len(v) for v in layers['landcover'].values())} land-cover areas, {len(layers['university'])} university areas")

    # ---- Rasters ----
    landcover = burn_layers(grid, layers["landcover"], osm_mod.LANDCOVER_ORDER, osm_mod.LANDCOVER, osm_mod.LANDCOVER["open"])
    paths = burn_layers(grid, layers["paths"], osm_mod.PATH_ORDER, osm_mod.PATHS, 0, all_touched=True)
    buildings = burn_mask(grid, [b["local"] for b in layers["buildings"]]).astype(np.uint8)
    ownership = np.zeros_like(landcover)
    ownership[burn_mask(grid, layers["town"])] = osm_mod.OWNERSHIP["town"]
    ownership[burn_mask(grid, layers["university"])] = osm_mod.OWNERSHIP["university"]
    ownership[burn_mask(grid, layers["protected"])] |= osm_mod.OWNERSHIP_PROTECTED_BIT

    rasters = {"landcover": landcover, "ownership": ownership, "paths": paths, "buildings": buildings}
    for name, arr in rasters.items():
        arr.astype(np.uint8).tofile(MAP_DIR / f"{prefix}_{name}.u8")
    print_stats(rasters)

    # ---- Vectors + metadata ----
    features = features_json(grid, layers)
    (MAP_DIR / f"{prefix}_features.json").write_text(json.dumps(features, separators=(",", ":")), encoding="utf-8")
    meta = metadata(grid, cfg, prefix, lo, hi, dem_info, raw, osm_path)
    (MAP_DIR / f"{prefix}_map.json").write_text(json.dumps(meta, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Wrote {prefix}_map.json, {prefix}_features.json, heightmap and {len(rasters)} rasters to {MAP_DIR.relative_to(REPO)}")

    if not args.no_previews:
        import previews
        PREVIEWS.mkdir(parents=True, exist_ok=True)
        previews.write_all(PREVIEWS, heights, rasters, grid)
        print(f"Previews in {PREVIEWS.relative_to(REPO)}")
    return 0


def classify(grid: MapGrid, cfg: dict, elements: list[dict]) -> dict:
    widths = cfg["line_widths_m"]
    square = box(-grid.half, -grid.half, grid.half, grid.half)
    landcover: dict[str, list] = {}
    paths: dict[str, list] = {}
    out = {"landcover": landcover, "paths": paths, "buildings": [], "road_features": [], "waterways": [],
           "university": [], "university_features": [], "town": [], "protected": []}

    for el in elements:
        tags = el.get("tags") or {}
        geom = osm_mod.element_geometry(el)
        if geom is None:
            continue
        local = to_local(grid, geom)
        if not local.intersects(square):
            continue
        osm_id = f"{el['type']}/{el['id']}"
        is_area = isinstance(local, (Polygon, MultiPolygon))

        if is_area and "building" in tags:
            if square.contains(local.centroid):
                out["buildings"].append({"osm_id": osm_id, "tags": tags, "local": local})
            continue
        if is_area and osm_mod.is_university(tags):
            out["university"].append(local)
            out["university_features"].append({"osm_id": osm_id, "name": tags.get("name"), "local": local})
        if is_area and osm_mod.is_city_limits(tags):
            out["town"].append(local)
        if is_area and osm_mod.is_protected(tags):
            out["protected"].append(local)
        if is_area and (cls := osm_mod.landcover_class(tags)):
            landcover.setdefault(cls, []).append(local)
            continue

        if isinstance(local, LineString):
            if cls := osm_mod.path_class(tags):
                paths.setdefault(cls, []).append(local.buffer(widths[cls] / 2))
                out["road_features"].append({"osm_id": osm_id, "class": cls, "tags": tags, "local": local})
            elif cls := osm_mod.waterway_class(tags):
                landcover.setdefault("water", []).append(local.buffer(widths[cls] / 2))
                out["waterways"].append({"osm_id": osm_id, "class": cls, "tags": tags, "local": local})
    return out


def _tile_coords(grid: MapGrid, coords) -> list[list[float]]:
    return [[round(tx, 2), round(ty, 2)] for tx, ty in (grid.local_to_tile(x, y) for x, y in coords)]


def _outline(grid: MapGrid, geom) -> list[list[float]]:
    poly = max(geom.geoms, key=lambda p: p.area) if isinstance(geom, MultiPolygon) else geom
    return _tile_coords(grid, list(poly.exterior.coords)[:-1])


def _lines(grid: MapGrid, geom, square) -> list[list[list[float]]]:
    clipped = geom.intersection(square)
    parts = clipped.geoms if isinstance(clipped, MultiLineString) else [clipped]
    return [_tile_coords(grid, p.coords) for p in parts if isinstance(p, LineString) and len(p.coords) >= 2]


def _num(value) -> float | None:
    try:
        return float(str(value).split(";")[0].replace("m", "").strip())
    except (TypeError, ValueError):
        return None


def features_json(grid: MapGrid, layers: dict) -> dict:
    square = box(-grid.half, -grid.half, grid.half, grid.half)
    buildings = [{
        "osm_id": b["osm_id"],
        "name": b["tags"].get("name"),
        "building": b["tags"].get("building"),
        "levels": _num(b["tags"].get("building:levels")),
        "height_m": _num(b["tags"].get("height")),
        "start_date": b["tags"].get("start_date"),
        "outline": _outline(grid, b["local"]),
    } for b in layers["buildings"]]
    roads = [{"osm_id": r["osm_id"], "class": r["class"], "highway": r["tags"].get("highway") or r["tags"].get("railway"),
              "name": r["tags"].get("name"), "lines": lines}
             for r in layers["road_features"] if (lines := _lines(grid, r["local"], square))]
    waterways = [{"osm_id": w["osm_id"], "class": w["class"], "name": w["tags"].get("name"), "lines": lines}
                 for w in layers["waterways"] if (lines := _lines(grid, w["local"], square))]
    university = [{"osm_id": u["osm_id"], "name": u["name"], "outline": _outline(grid, u["local"].intersection(square))}
                  for u in layers["university_features"]]
    return {
        "$schema": "../schemas/map_features.schema.json",
        "_comment": "Generated by tools/map_pipeline/build_map.py — do not edit by hand. Coordinates are tile units "
                    "(1 tile = map tile size) from the NW corner, +y south. Map data © OpenStreetMap contributors, ODbL 1.0.",
        "buildings": buildings,
        "roads": roads,
        "waterways": waterways,
        "university_areas": university,
    }


def metadata(grid: MapGrid, cfg: dict, prefix: str, lo: float, hi: float, dem_info: dict, raw_osm: dict, osm_path) -> dict:
    s, w, n, e = grid.lonlat_bbox(0)
    today = dt.date.today().isoformat()
    t, hs = grid.tiles, grid.height_samples
    return {
        "$schema": "../schemas/map_output.schema.json",
        "_comment": "Generated by tools/map_pipeline/build_map.py — do not edit by hand.",
        "generated": today,
        "center": {"lat": grid.center_lat, "lon": grid.center_lon},
        "projection": grid.proj4,
        "size_m": grid.size_m,
        "tile_size_m": grid.tile_m,
        "tiles": t,
        "lonlat_bounds": {"south": round(s, 6), "west": round(w, 6), "north": round(n, 6), "east": round(e, 6)},
        "axes": "tile x east, tile y south, origin at the NW corner; local metres: x east, y north from the centre",
        "heightmap": {
            "file": f"{prefix}_heightmap.r16", "width": hs, "height": hs, "resolution_m": grid.height_res_m,
            "format": "uint16_le", "samples_on": "tile corners",
            "elevation_min_m": round(lo, 4), "elevation_max_m": round(hi, 4),
            "decode": "elevation_m = elevation_min_m + value / 65535 * (elevation_max_m - elevation_min_m)",
            "vertical_datum": dem_info.get("vertical_datum", "NAVD 88 (3DEP)"),
        },
        "rasters": {
            name: {"file": f"{prefix}_{name}.u8", "width": t, "height": t, "format": "uint8"}
            for name in ("landcover", "ownership", "paths", "buildings")
        },
        "codes": {
            "landcover": osm_mod.LANDCOVER,
            "ownership": osm_mod.OWNERSHIP,
            "ownership_protected_bit": osm_mod.OWNERSHIP_PROTECTED_BIT,
            "paths": osm_mod.PATHS,
            "buildings": {"none": 0, "building": 1},
        },
        "features_file": f"{prefix}_features.json",
        "sources": {
            "elevation": {"provider": "USGS 3D Elevation Program (3DEP)", "license": "Public domain (U.S. Government work)",
                          "service": cfg["dem"]["service_url"], "retrieved": today, **dem_info},
            "osm": {"attribution": "© OpenStreetMap contributors", "license": "ODbL 1.0",
                    "license_url": "https://www.openstreetmap.org/copyright",
                    "data_timestamp": (raw_osm.get("osm3s") or {}).get("timestamp_osm_base"),
                    "retrieved": today, "input": pathlib.Path(osm_path).name},
        },
    }


def print_stats(rasters: dict[str, np.ndarray]) -> None:
    total = rasters["landcover"].size
    inv = {v: k for k, v in osm_mod.LANDCOVER.items()}
    counts = np.bincount(rasters["landcover"].ravel(), minlength=256)
    parts = [f"{inv.get(i, i)} {c / total:.0%}" for i, c in sorted(enumerate(counts), key=lambda x: -x[1]) if c]
    print("  land cover: " + ", ".join(parts))
    own = rasters["ownership"] & 0x7F
    print(f"  ownership: university {np.mean(own == 2):.0%}, town {np.mean(own == 1):.0%}, "
          f"protected {np.mean(rasters['ownership'] >= 128):.1%}")
    print(f"  paths/roads on {np.mean(rasters['paths'] > 0):.0%} of tiles; buildings on {np.mean(rasters['buildings'] > 0):.1%}")


if __name__ == "__main__":
    sys.exit(main())
