"""Builds the example kit piece for Step 4 (PLACEHOLDER geometry, not final art) and exports it.

    blender --background --factory-startup --python tools/blender/make_example_kit.py

kit_georgian_wall_window_3m: one Georgian Revival wall bay (§28.3): red brick, a 6-over-6 sash window with white
trim, limestone sill and lintel. Three hand-made LODs (_LOD0/_LOD1/_LOD2). Sizes come from art_pipeline.json
kit_grid. Pivot: bottom-left corner of the outer wall face; the outer face looks toward -Y in Blender
(+Z in Godot), the wall's thickness goes into +Y (the building's inside).

Writes assets/models/kit/georgian/kit_georgian_wall_window_3m.glb and the .blend source in art/blend/.
"""

import os
import sys

import bmesh
import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lh_export  # noqa: E402

NAME = "kit_georgian_wall_window_3m"

grid = lh_export.load_data("art_pipeline.json")["kit_grid"]
W, H, T = grid["module_width_m"], grid["storey_height_m"], grid["wall_thickness_m"]

# Window opening (placeholder proportions, not measured from a real building).
WIN_W, WIN_H, SILL_Z = 1.2, 2.0, 0.9
WX0, WX1 = (W - WIN_W) / 2, (W + WIN_W) / 2
WZ0, WZ1 = SILL_Z, SILL_Z + WIN_H
FRAME, RECESS = 0.08, 0.10  # frame bar width; how far the window sits behind the wall face


def box(bm, mat, x0, y0, z0, x1, y1, z1):
    """Axis-aligned box: 6 quads = 12 triangles."""
    v = [bm.verts.new(p) for p in [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
                                   (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]]
    for idx in [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]:
        bm.faces.new([v[i] for i in idx]).material_index = mat


def quad_front(bm, mat, x0, z0, x1, z1, y):
    """One-sided quad facing -Y (outwards): 2 triangles."""
    v = [bm.verts.new(p) for p in [(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)]]
    bm.faces.new(v).material_index = mat


def wall_with_opening(bm, mat):
    box(bm, mat, 0, 0, 0, W, T, WZ0)          # below the window
    box(bm, mat, 0, 0, WZ1, W, T, H)          # above
    box(bm, mat, 0, 0, WZ0, WX0, T, WZ1)      # left of it
    box(bm, mat, WX1, 0, WZ0, W, T, WZ1)      # right of it


def lod0(bm, m):
    wall_with_opening(bm, m["brick"])
    fy0, fy1 = RECESS, RECESS + 0.08
    box(bm, m["trim"], WX0, fy0, WZ0, WX0 + FRAME, fy1, WZ1)                  # frame
    box(bm, m["trim"], WX1 - FRAME, fy0, WZ0, WX1, fy1, WZ1)
    box(bm, m["trim"], WX0 + FRAME, fy0, WZ1 - FRAME, WX1 - FRAME, fy1, WZ1)
    box(bm, m["trim"], WX0 + FRAME, fy0, WZ0, WX1 - FRAME, fy1, WZ0 + FRAME)
    ix0, ix1, iz0, iz1 = WX0 + FRAME, WX1 - FRAME, WZ0 + FRAME, WZ1 - FRAME
    my0, my1 = RECESS + 0.02, RECESS + 0.06
    for i in (1, 2):                                                          # 6-over-6 muntins
        x = ix0 + (ix1 - ix0) * i / 3
        box(bm, m["trim"], x - 0.02, my0, iz0, x + 0.02, my1, iz1)
    zm = (iz0 + iz1) / 2
    box(bm, m["trim"], ix0, my0, zm - 0.03, ix1, my1, zm + 0.03)              # meeting rail
    for z in ((iz0 + zm) / 2, (zm + iz1) / 2):
        box(bm, m["trim"], ix0, my0, z - 0.015, ix1, my1, z + 0.015)
    quad_front(bm, m["glass"], ix0, iz0, ix1, iz1, RECESS + 0.05)
    box(bm, m["stone"], WX0 - 0.05, -0.06, WZ0 - 0.08, WX1 + 0.05, RECESS, WZ0)  # sill
    box(bm, m["stone"], WX0 - 0.1, -0.02, WZ1, WX1 + 0.1, 0.02, WZ1 + 0.22)      # flat-arch lintel


def lod1(bm, m):
    wall_with_opening(bm, m["brick"])
    quad_front(bm, m["trim"], WX0, WZ0, WX1, WZ1, RECESS + 0.08)              # frame as one flat panel
    quad_front(bm, m["glass"], WX0 + FRAME, WZ0 + FRAME, WX1 - FRAME, WZ1 - FRAME, RECESS + 0.07)
    box(bm, m["stone"], WX0 - 0.05, -0.06, WZ0 - 0.08, WX1 + 0.05, RECESS, WZ0)


def lod2(bm, m):
    box(bm, m["brick"], 0, 0, 0, W, T, H)
    quad_front(bm, m["glass"], WX0, WZ0, WX1, WZ1, -0.01)                     # window painted on the face


def build_object(name, builder, mats):
    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    slots = {}
    for key, mat_name in mats.items():
        mesh.materials.append(lh_export.palette_material(mat_name))
        slots[key] = len(mesh.materials) - 1
    bm = bmesh.new()
    builder(bm, slots)
    bm.to_mesh(mesh)
    bm.free()
    # Drop unused material slots so the validator only sees materials the LOD really uses.
    used = {p.material_index for p in mesh.polygons}
    for i in reversed(range(len(mesh.materials))):
        if i not in used:
            mesh.materials.pop(index=i)
    obj["lh_category"] = "kit"  # -> glTF extras -> Godot metadata
    return obj


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mats = {"brick": "pal_brick_1", "trim": "pal_trim", "glass": "mat_glass", "stone": "pal_limestone"}
    for level, builder in enumerate((lod0, lod1, lod2)):
        obj = build_object(f"{NAME}_LOD{level}", builder, mats)
        obj.location.x = 0  # all LODs share the pivot
        obj.hide_viewport = level > 0  # preview only; export includes hidden objects (use_visible=False)
    glb, blend = lh_export.output_paths(NAME)
    lh_export.export_glb(glb)
    os.makedirs(os.path.dirname(blend), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=blend)
    for obj in bpy.context.scene.objects:
        tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        print(f"LH_TRIS {obj.name} {tris}")


main()
