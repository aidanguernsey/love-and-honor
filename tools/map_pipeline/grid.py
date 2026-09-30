"""Map geometry: a local transverse-Mercator projection centred on campus, plus the tile and heightmap grids.

Coordinates used throughout the pipeline:
  lon/lat   WGS84 degrees (OSM, USGS queries)
  local x/y metres from the map centre; +x east, +y north (grid north == true north at the centre)
  tile x/y  0..tiles from the north-west corner; +x east, +y SOUTH (row-major, row 0 is the northern edge).
            Tile (i, j) covers [i, i+1) x [j, j+1); tile centres are at i + 0.5.
  heightmap samples sit on tile CORNERS every `height_res_m`: sample (0, 0) is the NW corner of the map.
The same conventions are used by the C# side (Godot +X = east, +Z = south = tile y).
"""
from __future__ import annotations

import json
import pathlib
from dataclasses import dataclass
from functools import cached_property

from affine import Affine
from pyproj import CRS, Transformer

REPO = pathlib.Path(__file__).resolve().parents[2]
DATA = REPO / "data"
MAP_DIR = DATA / "map"


@dataclass(frozen=True)
class MapGrid:
    center_lat: float
    center_lon: float
    size_m: float
    tile_m: float
    height_res_m: float

    @property
    def half(self) -> float:
        return self.size_m / 2

    @property
    def tiles(self) -> int:
        n = self.size_m / self.tile_m
        if abs(n - round(n)) > 1e-9:
            raise ValueError("map size must be a whole number of tiles")
        return int(round(n))

    @property
    def height_samples(self) -> int:
        n = self.size_m / self.height_res_m
        if abs(n - round(n)) > 1e-9:
            raise ValueError("map size must be a whole number of heightmap steps")
        return int(round(n)) + 1

    @property
    def proj4(self) -> str:
        return (f"+proj=tmerc +lat_0={self.center_lat} +lon_0={self.center_lon} +k=1 +x_0=0 +y_0=0 "
                "+datum=WGS84 +units=m +no_defs")

    @cached_property
    def crs(self) -> CRS:
        return CRS.from_proj4(self.proj4)

    @cached_property
    def _to_local(self) -> Transformer:
        return Transformer.from_crs("EPSG:4326", self.crs, always_xy=True)

    @cached_property
    def _to_lonlat(self) -> Transformer:
        return Transformer.from_crs(self.crs, "EPSG:4326", always_xy=True)

    def to_local(self, lon, lat):
        return self._to_local.transform(lon, lat)

    def to_lonlat(self, x, y):
        return self._to_lonlat.transform(x, y)

    def local_to_tile(self, x, y):
        return (x + self.half) / self.tile_m, (self.half - y) / self.tile_m

    def lonlat_to_tile(self, lon, lat):
        return self.local_to_tile(*self.to_local(lon, lat))

    @property
    def tile_transform(self) -> Affine:
        """Raster transform for the tile grid (pixel centres = tile centres)."""
        return Affine.translation(-self.half, self.half) @ Affine.scale(self.tile_m, -self.tile_m)

    @property
    def height_transform(self) -> Affine:
        """Raster transform for the heightmap: pixel CENTRES land on tile corners (so the edge pixels straddle the border)."""
        r = self.height_res_m
        return Affine.translation(-self.half - r / 2, self.half + r / 2) @ Affine.scale(r, -r)

    def lonlat_bbox(self, margin_m: float) -> tuple[float, float, float, float]:
        """(south, west, north, east) covering the map square plus a margin, for Overpass."""
        h = self.half + margin_m
        corners = [self.to_lonlat(x, y) for x in (-h, 0, h) for y in (-h, 0, h)]
        lons = [c[0] for c in corners]
        lats = [c[1] for c in corners]
        return min(lats), min(lons), max(lats), max(lons)


def load_json(path: pathlib.Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def load_config() -> dict:
    return load_json(MAP_DIR / "map_config.json")


def load_grid(config: dict | None = None) -> MapGrid:
    config = config or load_config()
    balance_map = load_json(DATA / "balance.json")["map"]
    return MapGrid(
        center_lat=config["center"]["lat"],
        center_lon=config["center"]["lon"],
        size_m=balance_map["extent_m"],
        tile_m=balance_map["tile_size_m"],
        height_res_m=config["heightmap_resolution_m"],
    )
