"""Unit tests for the map pipeline. Run: tools\\.venv\\Scripts\\python -m pytest tools\\map_pipeline"""
import numpy as np
import pytest
import rasterio
from rasterio.transform import from_origin
from shapely.geometry import LineString, Polygon, box

import dem
import osm
from grid import MapGrid, load_grid
from rasterize import burn_layers, burn_mask

GRID = MapGrid(center_lat=39.5087, center_lon=-84.7337, size_m=4000, tile_m=10, height_res_m=5)


# ---------------- grid ----------------

def test_grid_dimensions():
    assert GRID.tiles == 400
    assert GRID.height_samples == 801


def test_center_is_middle_tile_corner():
    tx, ty = GRID.lonlat_to_tile(GRID.center_lon, GRID.center_lat)
    assert tx == pytest.approx(200, abs=1e-6)
    assert ty == pytest.approx(200, abs=1e-6)


def test_tile_axes_east_and_south():
    assert GRID.local_to_tile(1000, 0) == pytest.approx((300, 200))   # 1 km east → +100 tiles x
    assert GRID.local_to_tile(0, 1000) == pytest.approx((200, 100))   # 1 km north → -100 tiles y
    assert GRID.local_to_tile(-2000, 2000) == pytest.approx((0, 0))   # NW corner


def test_lonlat_roundtrip_and_true_north():
    lon, lat = GRID.to_lonlat(0, 1000)
    assert lon == pytest.approx(GRID.center_lon, abs=1e-9)  # due north of the centre keeps the same longitude
    assert lat > GRID.center_lat
    x, y = GRID.to_local(lon, lat)
    assert (x, y) == pytest.approx((0, 1000), abs=1e-6)


def test_heightmap_samples_sit_on_tile_corners():
    t = GRID.height_transform
    x, y = t @ (0.5, 0.5)               # centre of pixel (0, 0)
    assert (x, y) == pytest.approx((-2000, 2000))
    x, y = t @ (800.5, 800.5)           # centre of the last pixel
    assert (x, y) == pytest.approx((2000, -2000))


def test_repo_config_loads():
    g = load_grid()
    assert g.tiles * g.tile_m == g.size_m


# ---------------- DEM ----------------

def test_quantize_roundtrip_precision():
    h = np.linspace(231.5, 296.8, 1000, dtype=np.float32).reshape(10, 100)
    q, lo, hi = dem.quantize(h)
    assert q.dtype == np.uint16 and q.min() == 0 and q.max() == 65535
    assert np.abs(dem.dequantize(q, lo, hi) - h).max() < (hi - lo) / 65535


def test_resample_aligns_with_grid(tmp_path):
    """A tilted plane in UTM must come out as the same plane on the heightmap: catches half-pixel and axis errors."""
    small = MapGrid(GRID.center_lat, GRID.center_lon, size_m=400, tile_m=10, height_res_m=5)
    epsg = 32616
    xmin, ymin, xmax, ymax = dem.download_bbox(small, epsg, margin_m=50)
    px = 2.0
    w, h = int((xmax - xmin) / px) + 1, int((ymax - ymin) / px) + 1
    xs = xmin + (np.arange(w) + 0.5) * px
    ys = ymax - (np.arange(h) + 0.5) * px
    X, Y = np.meshgrid(xs, ys)
    plane = (250 + 0.01 * (X - xmin) + 0.02 * (Y - ymin)).astype(np.float32)
    path = tmp_path / "plane.tif"
    with rasterio.open(path, "w", driver="GTiff", width=w, height=h, count=1, dtype="float32",
                       crs=f"EPSG:{epsg}", transform=from_origin(xmin, ymax, px, px)) as dst:
        dst.write(plane, 1)

    out = dem.resample_to_grid(small, [path])
    # Expected: evaluate the plane at each heightmap sample's UTM position.
    from pyproj import Transformer
    to_utm = Transformer.from_crs(small.crs, f"EPSG:{epsg}", always_xy=True)
    for (i, j) in [(0, 0), (40, 0), (0, 80), (80, 80), (40, 40)]:
        lx, ly = -small.half + i * small.height_res_m, small.half - j * small.height_res_m
        ux, uy = to_utm.transform(lx, ly)
        expected = 250 + 0.01 * (ux - xmin) + 0.02 * (uy - ymin)
        assert out[j, i] == pytest.approx(expected, abs=0.05), (i, j)


def test_resample_reports_missing_coverage(tmp_path):
    small = MapGrid(GRID.center_lat, GRID.center_lon, size_m=400, tile_m=10, height_res_m=5)
    path = tmp_path / "tiny.tif"
    with rasterio.open(path, "w", driver="GTiff", width=2, height=2, count=1, dtype="float32",
                       crs="EPSG:32616", transform=from_origin(0, 100, 50, 50)) as dst:
        dst.write(np.ones((2, 2), np.float32), 1)
    with pytest.raises(RuntimeError, match="does not cover"):
        dem.resample_to_grid(small, [path])


# ---------------- OSM ----------------

def _way(i, coords, tags=None):
    return {"type": "way", "id": i, "tags": tags or {}, "geometry": [{"lon": x, "lat": y} for x, y in coords]}


def test_multipolygon_with_split_outer_ring_and_hole():
    rel = {"type": "relation", "id": 1, "tags": {"type": "multipolygon", "natural": "wood"}, "members": [
        {"type": "way", "role": "outer", "geometry": [{"lon": 0, "lat": 0}, {"lon": 4, "lat": 0}, {"lon": 4, "lat": 4}]},
        {"type": "way", "role": "outer", "geometry": [{"lon": 4, "lat": 4}, {"lon": 0, "lat": 4}, {"lon": 0, "lat": 0}]},
        {"type": "way", "role": "inner", "geometry": [{"lon": 1, "lat": 1}, {"lon": 2, "lat": 1}, {"lon": 2, "lat": 2},
                                                       {"lon": 1, "lat": 2}, {"lon": 1, "lat": 1}]},
    ]}
    g = osm.element_geometry(rel)
    assert g.area == pytest.approx(16 - 1)


def test_closed_footway_loop_stays_a_line_but_building_is_area():
    ring = [(0, 0), (1, 0), (1, 1), (0, 1), (0, 0)]
    assert isinstance(osm.element_geometry(_way(1, ring, {"highway": "footway"})), LineString)
    assert isinstance(osm.element_geometry(_way(2, ring, {"building": "yes"})), Polygon)
    assert isinstance(osm.element_geometry(_way(3, ring, {"highway": "pedestrian", "area": "yes"})), Polygon)


@pytest.mark.parametrize("tags,expected", [
    ({"natural": "wood"}, "forest"), ({"landuse": "forest"}, "forest"), ({"landuse": "farmland"}, "farmland"),
    ({"landuse": "meadow"}, "grass"), ({"natural": "water"}, "water"), ({"leisure": "park"}, "recreation"),
    ({"landuse": "residential"}, "residential"), ({"landuse": "retail"}, "commercial"), ({"amenity": "cafe"}, None),
])
def test_landcover_classes(tags, expected):
    assert osm.landcover_class(tags) == expected


@pytest.mark.parametrize("tags,expected", [
    ({"highway": "footway"}, "footway"), ({"highway": "steps"}, "footway"), ({"highway": "trunk"}, "primary"),
    ({"highway": "residential"}, "residential"), ({"railway": "rail"}, "railway"), ({"railway": "abandoned"}, None),
    ({"highway": "proposed"}, None),
])
def test_path_classes(tags, expected):
    assert osm.path_class(tags) == expected


def test_query_has_bbox_and_keeps_relation_members():
    q = osm.build_query(GRID, 100)
    assert "{{bbox}}" not in q
    assert "out geom;" in q  # 'out geom tags' would drop relation members (a real bug we hit)


# ---------------- rasterize ----------------

def test_later_classes_win_and_areas_use_tile_centres():
    small = MapGrid(GRID.center_lat, GRID.center_lon, size_m=100, tile_m=10, height_res_m=5)  # 10x10 tiles
    forest = box(-50, -50, 50, 50)
    water = box(-10, -10, 10, 10)  # covers the 4 tiles around the centre
    out = burn_layers(small, {"forest": [forest], "water": [water]}, ["forest", "water"], osm.LANDCOVER)
    assert (out == osm.LANDCOVER["water"]).sum() == 4
    assert (out == osm.LANDCOVER["forest"]).sum() == 96


def test_thin_lines_touch_continuous_tiles():
    small = MapGrid(GRID.center_lat, GRID.center_lon, size_m=100, tile_m=10, height_res_m=5)
    path = LineString([(-50, -3), (50, -3)]).buffer(1.25)  # 2.5 m footway, off tile centres
    mask = burn_mask(small, [path], all_touched=True)
    assert mask.any(axis=0).all(), "path must be continuous across the map"
    assert not burn_mask(small, [path], all_touched=False).any(), "centre sampling would lose it"
