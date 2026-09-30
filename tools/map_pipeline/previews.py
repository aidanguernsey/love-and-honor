"""Human-readable preview images of the pipeline output (not used by the game)."""
from __future__ import annotations

import pathlib

import numpy as np
from PIL import Image

import osm as osm_mod
from grid import MapGrid

LANDCOVER_COLORS = {
    "open": (196, 214, 160), "forest": (58, 110, 58), "farmland": (226, 208, 140), "residential": (205, 190, 180),
    "commercial": (222, 170, 160), "industrial": (180, 170, 190), "recreation": (140, 200, 120), "water": (90, 150, 210),
    "wetland": (120, 170, 170), "education": (230, 200, 200), "cemetery": (170, 190, 160), "railway": (150, 140, 140),
    "grass": (170, 210, 130),
}
PATH_COLORS = {1: (140, 70, 50), 2: (60, 110, 200), 3: (200, 200, 200), 4: (250, 250, 250), 5: (255, 230, 150),
               6: (250, 200, 110), 7: (240, 150, 90), 8: (80, 80, 80)}


def hillshade(heights: np.ndarray, res_m: float, azimuth_deg: float = 315, altitude_deg: float = 45) -> np.ndarray:
    dy, dx = np.gradient(heights.astype(np.float64), res_m)
    slope = np.arctan(np.hypot(dx, dy))
    aspect = np.arctan2(-dx, dy)
    az, alt = np.radians(360 - azimuth_deg + 90), np.radians(altitude_deg)
    shade = np.sin(alt) * np.cos(slope) + np.cos(alt) * np.sin(slope) * np.cos(az - aspect)
    return np.clip(shade * 255, 0, 255).astype(np.uint8)


def write_all(out_dir: pathlib.Path, heights: np.ndarray, rasters: dict, grid: MapGrid) -> None:
    shade = hillshade(heights, grid.height_res_m)
    norm = (heights - heights.min()) / max(np.ptp(heights), 1e-6)
    tint = np.stack([90 + 120 * norm, 140 + 80 * norm, 90 + 40 * norm], axis=-1)
    relief = (tint * (0.35 + 0.65 * shade[..., None] / 255)).clip(0, 255).astype(np.uint8)
    Image.fromarray(relief).save(out_dir / "heightmap_relief.png")

    inv = {v: k for k, v in osm_mod.LANDCOVER.items()}
    lc = rasters["landcover"]
    img = np.zeros(lc.shape + (3,), dtype=np.uint8)
    for code, name in inv.items():
        img[lc == code] = LANDCOVER_COLORS[name]
    own = rasters["ownership"] & 0x7F
    img[own == 2] = (img[own == 2] * 0.75 + np.array([195, 20, 45]) * 0.25).astype(np.uint8)  # university tint
    for code, color in PATH_COLORS.items():
        img[rasters["paths"] == code] = color
    img[rasters["buildings"] > 0] = (60, 45, 45)
    Image.fromarray(img).resize((lc.shape[1] * 2, lc.shape[0] * 2), Image.NEAREST).save(out_dir / "layers.png")
