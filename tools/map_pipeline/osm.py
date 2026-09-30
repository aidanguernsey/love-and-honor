"""OpenStreetMap: fetch via Overpass (or read a local export), build shapely geometries, classify tags."""
from __future__ import annotations

import json
import pathlib
import time

import requests
from shapely.geometry import LineString, MultiPolygon, Polygon
from shapely.ops import linemerge, polygonize, unary_union

from grid import MapGrid
from net import USER_AGENT, cached_name

QUERY_FILE = pathlib.Path(__file__).with_name("overpass_query.overpassql")

# ---------------- classification codes (mirrored in data/map/<prefix>_map.json and the C# loader) ----------------

LANDCOVER = {
    "open": 0, "forest": 1, "farmland": 2, "residential": 3, "commercial": 4, "industrial": 5,
    "recreation": 6, "water": 7, "wetland": 8, "education": 9, "cemetery": 10, "railway": 11, "grass": 12,
}
# Low byte values; OWNERSHIP_PROTECTED_BIT is OR-ed in for protected areas / nature reserves.
OWNERSHIP = {"private": 0, "town": 1, "university": 2}
OWNERSHIP_PROTECTED_BIT = 128
PATHS = {"none": 0, "footway": 1, "cycleway": 2, "service": 3, "residential": 4, "tertiary": 5,
         "secondary": 6, "primary": 7, "railway": 8}

# Later entries overwrite earlier ones when rasterizing (physical cover beats zoning).
LANDCOVER_ORDER = ["residential", "commercial", "industrial", "farmland", "grass", "education", "cemetery",
                   "recreation", "railway", "forest", "wetland", "water"]
PATH_ORDER = ["footway", "cycleway", "service", "residential", "tertiary", "secondary", "primary", "railway"]

_HIGHWAY_CLASS = {
    "footway": "footway", "path": "footway", "pedestrian": "footway", "steps": "footway", "corridor": "footway",
    "bridleway": "footway", "cycleway": "cycleway",
    "service": "service", "track": "service",
    "residential": "residential", "unclassified": "residential", "living_street": "residential", "road": "residential",
    "tertiary": "tertiary", "tertiary_link": "tertiary",
    "secondary": "secondary", "secondary_link": "secondary",
    "primary": "primary", "primary_link": "primary", "trunk": "primary", "trunk_link": "primary",
    "motorway": "primary", "motorway_link": "primary",
}


def landcover_class(tags: dict) -> str | None:
    lu, nat, lei = tags.get("landuse"), tags.get("natural"), tags.get("leisure")
    if nat == "water" or lu in ("reservoir", "basin") or tags.get("waterway") == "riverbank":
        return "water"
    if nat == "wetland":
        return "wetland"
    if nat in ("wood", "scrub") or lu == "forest":
        return "forest"
    if lu in ("farmland", "orchard", "farmyard", "vineyard", "plant_nursery"):
        return "farmland"
    if lu in ("meadow", "grass", "village_green", "recreation_ground") or nat == "grassland":
        return "grass"
    if lei in ("park", "pitch", "golf_course", "garden", "stadium", "track", "playground", "sports_centre"):
        return "recreation"
    if lu == "residential":
        return "residential"
    if lu in ("commercial", "retail"):
        return "commercial"
    if lu in ("industrial", "construction", "brownfield", "garages"):
        return "industrial"
    if lu == "education":
        return "education"
    if lu == "cemetery" or tags.get("amenity") == "grave_yard":
        return "cemetery"
    if lu == "railway":
        return "railway"
    return None


def path_class(tags: dict) -> str | None:
    if tags.get("railway") in ("rail", "light_rail", "disused"):
        return "railway"
    return _HIGHWAY_CLASS.get(tags.get("highway", ""))


def waterway_class(tags: dict) -> str | None:
    w = tags.get("waterway")
    if w in ("stream", "ditch", "drain", "canal"):
        return "stream"
    if w == "river":
        return "river"
    return None


def is_university(tags: dict) -> bool:
    return tags.get("amenity") in ("university", "college")


def is_city_limits(tags: dict, city_name: str = "Oxford") -> bool:
    return tags.get("boundary") == "administrative" and tags.get("admin_level") == "8" and tags.get("name") == city_name


def is_protected(tags: dict) -> bool:
    return tags.get("leisure") == "nature_reserve" or tags.get("boundary") == "protected_area"


# ---------------- fetching ----------------

def build_query(grid: MapGrid, margin_m: float) -> str:
    s, w, n, e = grid.lonlat_bbox(margin_m)
    return QUERY_FILE.read_text(encoding="utf-8").replace("{{bbox}}", f"{s:.6f},{w:.6f},{n:.6f},{e:.6f}")


def fetch_osm(grid: MapGrid, cfg: dict, cache_dir: pathlib.Path, refresh: bool = False) -> pathlib.Path:
    query = build_query(grid, cfg["osm"]["margin_m"])
    out = cache_dir / cached_name("osm", query, ".json")
    if out.exists() and not refresh:
        print(f"  OSM: using cached {out.name}")
        return out
    data = _query_overpass(query, cfg["osm"]["overpass_urls"], cfg["osm"]["retries_per_server"])
    cache_dir.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(data), encoding="utf-8")
    print(f"  OSM: saved {out.name} ({len(data.get('elements', []))} elements, {out.stat().st_size / 1e6:.1f} MB)")
    return out


def _query_overpass(query: str, urls: list[str], retries: int) -> dict:
    """Public Overpass servers are often busy (429/504): retry with backoff, then try the next server."""
    errors = []
    for url in urls:
        for attempt in range(retries):
            print(f"  OSM: querying {url} (attempt {attempt + 1}/{retries}; can take a minute) ...")
            try:
                r = requests.post(url, data={"data": query}, headers={"User-Agent": USER_AGENT}, timeout=300)
                if r.status_code in (429, 502, 503, 504):
                    raise requests.HTTPError(f"HTTP {r.status_code}", response=r)
                r.raise_for_status()
                data = r.json()
                if data.get("remark") and not data.get("elements"):
                    raise RuntimeError(f"Overpass remark: {data['remark']}")
                return data
            except (requests.RequestException, ValueError, RuntimeError) as e:
                errors.append(f"{url}: {e}")
                time.sleep(10 * (attempt + 1))
    raise RuntimeError("All Overpass servers failed. Use --osm-file with a manual export (see tools/map_pipeline/README.md).\n  "
                       + "\n  ".join(errors))


def load_elements(path: pathlib.Path) -> list[dict]:
    data = json.loads(path.read_text(encoding="utf-8"))
    elements = data.get("elements")
    if elements is None:
        raise ValueError(f"{path}: not Overpass JSON (no 'elements'). Export 'raw OSM data' as JSON from overpass-turbo.")
    return elements


# ---------------- geometry ----------------

_AREA_KEYS = ("building", "landuse", "natural", "leisure", "amenity", "boundary")


def _coords(geometry: list[dict]) -> list[tuple[float, float]]:
    return [(p["lon"], p["lat"]) for p in geometry if p is not None]


def _is_area_way(tags: dict, coords: list) -> bool:
    if len(coords) < 4 or coords[0] != coords[-1]:
        return False
    if tags.get("area") == "yes":
        return True
    if "highway" in tags or "railway" in tags or "waterway" in tags:
        return tags.get("area") == "yes"  # closed footway loops etc. stay lines
    return any(k in tags for k in _AREA_KEYS)


def _rings(members: list[dict], role: str) -> list[Polygon]:
    lines = [LineString(_coords(m["geometry"])) for m in members
             if m.get("type") == "way" and m.get("role", "") == role and len(m.get("geometry") or []) >= 2]
    if not lines:
        return []
    merged = linemerge(lines)
    return [p for p in polygonize(merged) if p.is_valid and not p.is_empty]


def element_geometry(el: dict):
    """Shapely geometry in lon/lat, or None. Ways → Polygon (areas) / LineString; multipolygon relations → (Multi)Polygon."""
    tags = el.get("tags", {})
    if el["type"] == "way":
        coords = _coords(el.get("geometry") or [])
        if len(coords) < 2:
            return None
        if _is_area_way(tags, coords):
            poly = Polygon(coords)
            return poly if poly.is_valid else poly.buffer(0)
        return LineString(coords)
    if el["type"] == "relation" and tags.get("type") in ("multipolygon", "boundary"):
        members = el.get("members") or []
        outers = _rings(members, "outer")
        if not outers:
            return None
        inners = _rings(members, "inner")
        shape = unary_union(outers)
        if inners:
            shape = shape.difference(unary_union(inners))
        return shape if isinstance(shape, (Polygon, MultiPolygon)) and not shape.is_empty else None
    return None
