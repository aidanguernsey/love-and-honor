"""Elevation: download a USGS 3DEP extract (or use local GeoTIFFs), resample onto the heightmap grid, quantize to 16 bits."""
from __future__ import annotations

import math
import pathlib

import numpy as np
import rasterio
import requests
from pyproj import Transformer
from rasterio.warp import Resampling, reproject

from grid import MapGrid
from net import USER_AGENT, cached_name


def download_bbox(grid: MapGrid, epsg: int, margin_m: float) -> tuple[float, float, float, float]:
    """Bounds (xmin, ymin, xmax, ymax) in `epsg` covering the map square plus a margin."""
    to_dl = Transformer.from_crs(grid.crs, f"EPSG:{epsg}", always_xy=True)
    h = grid.half + margin_m
    pts = [to_dl.transform(x, y) for x in np.linspace(-h, h, 9) for y in np.linspace(-h, h, 9)]
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    return min(xs), min(ys), max(xs), max(ys)


def fetch_dem(grid: MapGrid, cfg: dict, cache_dir: pathlib.Path, refresh: bool = False) -> pathlib.Path:
    """Downloads a float32 GeoTIFF from the 3DEP ImageServer (best available source, e.g. 1 m lidar), cached."""
    dem = cfg["dem"]
    epsg, px = dem["download_crs_epsg"], dem["download_pixel_m"]
    xmin, ymin, xmax, ymax = download_bbox(grid, epsg, dem["download_margin_m"])
    width, height = math.ceil((xmax - xmin) / px), math.ceil((ymax - ymin) / px)
    xmax, ymax = xmin + width * px, ymin + height * px
    params = {
        "bbox": f"{xmin:.3f},{ymin:.3f},{xmax:.3f},{ymax:.3f}",
        "bboxSR": epsg,
        "imageSR": epsg,
        "size": f"{width},{height}",
        "format": "tiff",
        "pixelType": "F32",
        "noDataInterpretation": "esriNoDataMatchAny",
        "interpolation": "RSP_BilinearInterpolation",
        "f": "image",
    }
    out = cache_dir / cached_name("dem", params, ".tif")
    if out.exists() and not refresh:
        print(f"  DEM: using cached {out.name}")
        return out
    print(f"  DEM: requesting {width}x{height} px at {px} m from USGS 3DEP ...")
    r = requests.get(dem["service_url"] + "/exportImage", params=params, headers={"User-Agent": USER_AGENT}, timeout=300)
    r.raise_for_status()
    if "tiff" not in r.headers.get("Content-Type", "") and not r.content[:4] in (b"II*\x00", b"MM\x00*"):
        raise RuntimeError(f"USGS returned {r.headers.get('Content-Type')}: {r.text[:300]}")
    cache_dir.mkdir(parents=True, exist_ok=True)
    out.write_bytes(r.content)
    print(f"  DEM: saved {out.name} ({len(r.content) / 1e6:.1f} MB)")
    return out


def dem_source_info(cfg: dict, grid: MapGrid) -> dict:
    """Which 3DEP dataset the service uses at the map centre (for attribution / the metadata file)."""
    geometry = f'{{"x":{grid.center_lon},"y":{grid.center_lat},"spatialReference":{{"wkid":4326}}}}'
    try:
        r = requests.get(
            cfg["dem"]["service_url"] + "/identify",
            params={"geometry": geometry, "geometryType": "esriGeometryPoint", "returnCatalogItems": "true",
                    "returnGeometry": "false", "maxItemCount": 100, "f": "json"},
            headers={"User-Agent": USER_AGENT}, timeout=60)
        r.raise_for_status()
        items = [f["attributes"] for f in (r.json().get("catalogItems") or {}).get("features", [])
                 if f["attributes"].get("Category") == 1]
        best = min(items, key=lambda a: a.get("LowPS", 1e9)) if items else None
    except (requests.RequestException, ValueError) as e:
        return {"note": f"source lookup failed: {e}"}
    if not best:
        return {"note": "no primary catalog item found"}
    return {
        "title": best.get("title"),
        "resolution_m": best.get("LowPS"),
        "vertical_datum": best.get("VerticalDatum"),
        "acquired": f"{best.get('StartDate')}–{best.get('EndDate')}",
    }


def resample_to_grid(grid: MapGrid, sources: list[pathlib.Path]) -> np.ndarray:
    """Averages the source DEM(s) onto the heightmap grid (tile corners every height_res_m). Later files fill gaps left by earlier ones."""
    n = grid.height_samples
    result = np.full((n, n), np.nan, dtype=np.float32)
    for path in sources:
        with rasterio.open(path) as src:
            tmp = np.full((n, n), np.nan, dtype=np.float32)
            reproject(
                source=rasterio.band(src, 1),
                destination=tmp,
                src_nodata=src.nodata,
                dst_transform=grid.height_transform,
                dst_crs=grid.crs,
                dst_nodata=np.nan,
                resampling=Resampling.average,
            )
        # Treat absurd values (service no-data sentinels) as missing.
        tmp[(tmp < -500) | (tmp > 9000)] = np.nan
        result = np.where(np.isnan(result), tmp, result)
    missing = int(np.isnan(result).sum())
    if missing:
        raise RuntimeError(f"DEM does not cover the map: {missing} of {n * n} heightmap samples missing")
    return result


def quantize(heights: np.ndarray) -> tuple[np.ndarray, float, float]:
    """Maps [min, max] to 0..65535. Returns (uint16 array, min_m, max_m). Precision = (max-min)/65535."""
    lo, hi = float(heights.min()), float(heights.max())
    span = max(hi - lo, 1e-6)
    q = np.round((heights - lo) / span * 65535.0).astype(np.uint16)
    return q, lo, hi


def dequantize(q: np.ndarray, lo: float, hi: float) -> np.ndarray:
    return lo + q.astype(np.float64) / 65535.0 * (hi - lo)
