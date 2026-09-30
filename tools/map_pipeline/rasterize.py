"""Burn classified shapes into the tile grid (one byte per tile)."""
from __future__ import annotations

import numpy as np
import shapely
from rasterio.features import rasterize

from grid import MapGrid


def to_local(grid: MapGrid, geom):
    """lon/lat shapely geometry → local metres."""
    return shapely.transform(geom, lambda x, y: grid.to_local(x, y), interleaved=False)


def burn_layers(grid: MapGrid, shapes: dict[str, list], order: list[str], codes: dict[str, int],
                base: int = 0, all_touched: bool = False) -> np.ndarray:
    """Rasterizes each class in `order` (later classes overwrite earlier ones). Shapes must be in local metres.
    all_touched=False marks tiles whose CENTRE is inside a shape (areas); True marks every tile touched (thin lines)."""
    n = grid.tiles
    out = np.full((n, n), base, dtype=np.uint8)
    for cls in order:
        geoms = [g for g in shapes.get(cls, []) if g is not None and not g.is_empty]
        if geoms:
            rasterize(((g, codes[cls]) for g in geoms), out=out, transform=grid.tile_transform, all_touched=all_touched)
    return out


def burn_mask(grid: MapGrid, geoms: list, all_touched: bool = False) -> np.ndarray:
    n = grid.tiles
    geoms = [g for g in geoms if g is not None and not g.is_empty]
    if not geoms:
        return np.zeros((n, n), dtype=bool)
    return rasterize(((g, 1) for g in geoms), out_shape=(n, n), transform=grid.tile_transform,
                     all_touched=all_touched, fill=0, dtype=np.uint8).astype(bool)
