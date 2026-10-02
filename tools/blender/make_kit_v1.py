"""Builds the Phase 1 (1f) Georgian kit v1: scripted PLACEHOLDER pieces (not final art) for the kit assembler.

    blender --background --factory-startup --python tools/blender/make_kit_v1.py

Conventions (docs/ART_PIPELINE.md §4, §8): wall-type pieces have their pivot at the bottom-left corner of the outer
wall face; the outer face looks toward -Y in Blender (+Z in Godot) and the building's inside is +Y. Free-standing pieces
(columns, cupola, chimney, steps, pediment) have their pivot at the bottom centre (steps and pediment: on the wall line,
reaching outward to -Y). Only faces that can be seen are modelled: the assembler closes the building, so wall pieces
have no back or side faces. Window glass is one quad with 0..1 UVs; the panes and muntins are drawn by
assets/shaders/window.gdshader (§28.1a), so any pane pattern costs the same 2 triangles.

Pieces stay stretchable along X (no detail within 0.25 m of a bay's side edges), as the assembler fits whole bays to
each wall. Writes assets/models/kit/georgian/<name>.glb (one _LOD0 each) and the .blend sources in art/blend/.
"""

import math
import os
import sys

import bmesh
import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lh_export  # noqa: E402

grid = lh_export.load_data("art_pipeline.json")["kit_grid"]
W, H = grid["module_width_m"], grid["storey_height_m"]
R = 0.12   # window/door recess behind the wall face
F = 0.09   # frame width

MATS = {"brick": "pal_brick_1", "trim": "pal_trim", "glass": "mat_glass", "stone": "pal_limestone",
        "slate": "pal_slate", "copper": "pal_copper", "iron": "mat_iron"}


class Piece:
    """Collects faces for one piece; each face is a list of points (counter-clockwise seen from outside)."""

    def __init__(self):
        self.faces = []  # (material key, points, uvs or None)

    def face(self, mat, pts, uvs=None):
        self.faces.append((mat, pts, uvs))

    def quad_y(self, mat, x0, z0, x1, z1, y, facing=-1, uv=False):
        """Quad in the plane y = const, facing -Y (facing=-1, outward) or +Y."""
        pts = [(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)]
        uvs = [(0, 0), (1, 0), (1, 1), (0, 1)] if uv else None
        if facing > 0:
            pts.reverse()
            uvs = list(reversed(uvs)) if uvs else None
        self.face(mat, pts, uvs)

    def quad_z(self, mat, x0, y0, x1, y1, z, facing=1):
        """Horizontal quad facing up (+Z) or down."""
        pts = [(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)]
        self.face(mat, pts if facing > 0 else list(reversed(pts)))

    def quad_x(self, mat, y0, z0, y1, z1, x, facing=-1):
        """Quad in the plane x = const, facing -X or +X."""
        pts = [(x, y0, z0), (x, y0, z1), (x, y1, z1), (x, y1, z0)]  # faces -X
        self.face(mat, pts if facing < 0 else list(reversed(pts)))

    def box(self, mat, x0, y0, z0, x1, y1, z1, skip=()):
        """Axis-aligned box; skip any of 'top', 'bottom', 'front' (-Y), 'back', 'left' (-X), 'right'."""
        if "front" not in skip: self.quad_y(mat, x0, z0, x1, z1, y0, -1)
        if "back" not in skip: self.quad_y(mat, x0, z0, x1, z1, y1, +1)
        if "left" not in skip: self.quad_x(mat, y0, z0, y1, z1, x0, -1)
        if "right" not in skip: self.quad_x(mat, y0, z0, y1, z1, x1, +1)
        if "top" not in skip: self.quad_z(mat, x0, y0, x1, y1, z1, +1)
        if "bottom" not in skip: self.quad_z(mat, x0, y0, x1, y1, z0, -1)

    def prism(self, mat, cx, cy, z0, z1, r, sides=8, top=False):
        """Upright n-sided prism around (cx, cy)."""
        ring = [(cx + r * math.cos(2 * math.pi * (i + 0.5) / sides), cy + r * math.sin(2 * math.pi * (i + 0.5) / sides)) for i in range(sides)]
        for i in range(sides):
            (ax, ay), (bx, by) = ring[i], ring[(i + 1) % sides]
            self.face(mat, [(ax, ay, z0), (bx, by, z0), (bx, by, z1), (ax, ay, z1)])
        if top:
            self.face(mat, [(x, y, z1) for x, y in ring])

    def pyramid(self, mat, cx, cy, z0, z1, r, sides=8):
        ring = [(cx + r * math.cos(2 * math.pi * (i + 0.5) / sides), cy + r * math.sin(2 * math.pi * (i + 0.5) / sides)) for i in range(sides)]
        for i in range(sides):
            (ax, ay), (bx, by) = ring[i], ring[(i + 1) % sides]
            self.face(mat, [(ax, ay, z0), (bx, by, z0), (cx, cy, z1)])


def wall_with_hole(p, mat, x0, z0, x1, z1, sill=True):
    """The outer wall face of one bay with a rectangular opening (x0..x1, z0..z1)."""
    if sill and z0 > 0: p.quad_y(mat, 0, 0, W, z0, 0)
    p.quad_y(mat, 0, z1, W, H, 0)
    p.quad_y(mat, 0, z0, x0, z1, 0)
    p.quad_y(mat, x1, z0, W, z1, 0)


def reveal(p, mat, x0, z0, x1, z1, bottom=True):
    """The sides of the opening, from the wall face back to the recess."""
    if bottom: p.quad_z(mat, x0, 0, x1, R, z0, +1)
    p.quad_z(mat, x0, 0, x1, R, z1, -1)
    p.quad_x(mat, 0, z0, R, z1, x0, +1)
    p.quad_x(mat, 0, z0, R, z1, x1, -1)


def frame_ring(p, mat, x0, z0, x1, z1, y):
    """White frame around an opening (door); window frames are drawn by the window shader."""
    p.quad_y(mat, x0, z0, x0 + F, z1, y)
    p.quad_y(mat, x1 - F, z0, x1, z1, y)
    p.quad_y(mat, x0 + F, z1 - F, x1 - F, z1, y)
    p.quad_y(mat, x0 + F, z0, x1 - F, z0 + F, y)


# ---------------------------------------------------------------- pieces

def wall_plain_3m(p):
    p.quad_y("brick", 0, 0, W, H, 0)


def wall_opening_window_3m(p):
    x0, x1, z0, z1 = (W - 1.2) / 2, (W + 1.2) / 2, 0.9, 2.9
    wall_with_hole(p, "brick", x0, z0, x1, z1)
    reveal(p, "brick", x0, z0, x1, z1)
    p.quad_y("glass", x0, z0, x1, z1, R, uv=True)                       # frame, sashes and panes: window shader
    p.quad_y("stone", x0 - 0.08, z0 - 0.1, x1 + 0.08, z0, -0.06)        # sill
    p.quad_z("stone", x0 - 0.08, -0.06, x1 + 0.08, 0, z0, +1)
    p.quad_y("stone", x0 - 0.12, z1, x1 + 0.12, z1 + 0.25, -0.02)       # flat-arch lintel
    p.quad_z("stone", x0 - 0.12, -0.02, x1 + 0.12, 0, z1, -1)


def wall_opening_window_3m_lod1(p):
    """Flat facade for middle distances: the wall and the window (shader) on one plane."""
    x0, x1, z0, z1 = (W - 1.2) / 2, (W + 1.2) / 2, 0.9, 2.9
    p.quad_y("brick", 0, 0, W, H, 0)
    p.quad_y("glass", x0, z0, x1, z1, -0.02, uv=True)


def wall_opening_door_3m_lod1(p):
    x0, x1, z1, fan = (W - 1.5) / 2, (W + 1.5) / 2, 2.95, 2.45
    p.quad_y("brick", 0, 0, W, H, 0)
    p.quad_y("slate", x0, 0, x1, fan, -0.02)
    p.quad_y("glass", x0, fan, x1, z1, -0.02, uv=True)


def wall_opening_door_3m(p):
    x0, x1, z1, fan = (W - 1.5) / 2, (W + 1.5) / 2, 2.95, 2.45
    wall_with_hole(p, "brick", x0, 0, x1, z1, sill=False)
    reveal(p, "brick", x0, 0, x1, z1, bottom=False)
    frame_ring(p, "trim", x0, 0, x1, z1, R - 0.01)
    p.quad_y("trim", x0 + F, fan - 0.05, x1 - F, fan + 0.03, R - 0.01)  # transom bar
    p.quad_y("slate", x0 + F, 0, x1 - F, fan - 0.05, R)                 # door leaves (painted dark)
    p.quad_y("glass", x0 + F, fan + 0.03, x1 - F, z1 - F, R, uv=True)   # fanlight
    p.quad_y("trim", x0 - 0.2, z1 + 0.05, x1 + 0.2, z1 + 0.35, -0.1)    # hood
    p.quad_z("trim", x0 - 0.2, -0.1, x1 + 0.2, 0, z1 + 0.05, -1)
    p.quad_z("trim", x0 - 0.2, -0.1, x1 + 0.2, 0, z1 + 0.35, +1)


def wall_base_3m(p):
    """Water table: a projecting stone band along the bottom of the wall."""
    p.quad_y("stone", 0, 0, W, 0.6, -0.06)
    p.face("stone", [(0, -0.06, 0.6), (W, -0.06, 0.6), (W, 0, 0.7), (0, 0, 0.7)])


def corner_quoin(p):
    """Limestone quoins at an outer corner, one storey tall. The outgoing wall runs along +X from the pivot; the
    incoming wall lies in the plane x = 0 and runs into +Y."""
    p.quad_y("stone", -0.03, 0, 0.45, H, -0.03)
    p.quad_x("stone", -0.03, 0, 0.45, H, -0.03, -1)


def cornice_3m(p):
    p.quad_y("trim", 0, 0, W, 0.4, -0.35)
    p.quad_z("trim", 0, -0.35, W, 0, 0, -1)
    p.quad_z("trim", 0, -0.35, W, 0, 0.4, +1)


def cornice_corner(p):
    """Fills the outer corner where two cornices meet (same profile, outside the walls)."""
    p.box("trim", -0.35, -0.35, 0, 0, 0, 0.4, skip=("back", "right"))


def beltcourse_3m(p):
    p.quad_y("stone", 0, 0, W, 0.22, -0.05)


def column_doric(p):
    """6 m column (the assembler scales it to the portico height)."""
    p.box("trim", -0.32, -0.32, 0, 0.32, 0.32, 0.25, skip=("bottom",))
    p.prism("trim", 0, 0, 0.25, 5.75, 0.24)
    p.box("trim", -0.34, -0.34, 5.75, 0.34, 0.34, 6.0, skip=("top",))


def pediment_3bay(p):
    """Portico roof for three bays: entablature + triangular pediment + slate slopes. Pivot at the bottom centre of
    the wall line; the portico reaches 2.6 m out (-Y). The assembler scales X for narrower porticos."""
    w, d, e = 4.8, 2.6, 0.8
    h = w * math.tan(math.radians(22))
    p.box("trim", -w, -d, 0, w, 0, e, skip=("top", "back"))
    p.face("trim", [(-w, -d, e), (w, -d, e), (0, -d, e + h)])                     # tympanum
    p.face("slate", [(-w - 0.1, -d - 0.15, e), (0, -d - 0.15, e + h), (0, 0, e + h), (-w - 0.1, 0, e)])
    p.face("slate", [(0, -d - 0.15, e + h), (w + 0.1, -d - 0.15, e), (w + 0.1, 0, e), (0, 0, e + h)])


def portico_steps_3m(p):
    for i in range(3):
        y0, z0, z1 = -(3 - i) * 0.35, i * 0.16, (i + 1) * 0.16
        p.box("stone", -1.5 - 0.3 * (2 - i), y0, z0, 1.5 + 0.3 * (2 - i), 0, z1, skip=("back", "bottom"))


def cupola_small(p):
    """Square base, octagonal drum (dark louvres suggested by the iron band) and a copper dome."""
    p.box("trim", -1.2, -1.2, 0, 1.2, 1.2, 1.0, skip=("bottom",))
    p.prism("trim", 0, 0, 1.0, 2.4, 0.9)
    p.prism("iron", 0, 0, 1.35, 2.05, 0.92)
    p.pyramid("copper", 0, 0, 2.4, 3.9, 1.05)
    p.box("iron", -0.05, -0.05, 3.7, 0.05, 0.05, 4.6, skip=("bottom",))


def chimney_single(p):
    p.box("brick", -0.45, -0.3, 0, 0.45, 0.3, 2.8, skip=("bottom", "top"))
    p.box("stone", -0.52, -0.37, 2.8, 0.52, 0.37, 2.95, skip=())


# Pieces with a second, flat level of detail for middle distances (_LOD1). Pieces without one are either kept at that
# distance or dropped by the assembler's renderer (small details such as quoins and belt courses).
LOD1 = {
    "kit_georgian_wall_opening_window_3m": wall_opening_window_3m_lod1,
    "kit_georgian_wall_opening_door_3m": wall_opening_door_3m_lod1,
}

PIECES = {
    "kit_georgian_wall_plain_3m": wall_plain_3m,
    "kit_georgian_wall_opening_window_3m": wall_opening_window_3m,
    "kit_georgian_wall_opening_door_3m": wall_opening_door_3m,
    "kit_georgian_wall_base_3m": wall_base_3m,
    "kit_georgian_corner_quoin": corner_quoin,
    "kit_georgian_cornice_3m": cornice_3m,
    "kit_georgian_cornice_corner": cornice_corner,
    "kit_georgian_beltcourse_3m": beltcourse_3m,
    "kit_georgian_column_doric": column_doric,
    "kit_georgian_pediment_3bay": pediment_3bay,
    "kit_georgian_portico_steps_3m": portico_steps_3m,
    "kit_georgian_cupola_small": cupola_small,
    "kit_georgian_chimney_single": chimney_single,
}


def add_object(name, fn, level):
    p = Piece()
    fn(p)
    mesh = bpy.data.meshes.new(f"{name}_LOD{level}")
    obj = bpy.data.objects.new(f"{name}_LOD{level}", mesh)
    bpy.context.scene.collection.objects.link(obj)
    used = []
    for mat, _, _ in p.faces:
        if mat not in used:
            used.append(mat)
    for mat in used:
        mesh.materials.append(lh_export.palette_material(MATS[mat]))
    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.new("UVMap")
    for mat, pts, uvs in p.faces:
        verts = [bm.verts.new(pt) for pt in pts]
        f = bm.faces.new(verts)
        f.material_index = used.index(mat)
        for i, loop in enumerate(f.loops):
            loop[uv_layer].uv = uvs[i] if uvs else (0.0, 0.0)
    bm.to_mesh(mesh)
    bm.free()
    obj["lh_category"] = "kit"
    obj.hide_viewport = level > 0
    tris = sum(len(poly.vertices) - 2 for poly in mesh.polygons)
    print(f"LH_TRIS {name}_LOD{level} {tris}")


def build(name, fn):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    add_object(name, fn, 0)
    if name in LOD1:
        add_object(name, LOD1[name], 1)
    glb, blend = lh_export.output_paths(name)
    lh_export.export_glb(glb)
    os.makedirs(os.path.dirname(blend), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=blend)


for piece_name, builder in PIECES.items():
    build(piece_name, builder)
