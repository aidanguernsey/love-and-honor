"""Exports an artist's .blend to its standard .glb location (docs/ART_PIPELINE.md).

    blender --background art/blend/kit/georgian/kit_georgian_wall_window_3m.blend --python tools/blender/export_blend.py

The output path comes from the .blend file name (its category prefix picks the folder, see art_pipeline.json).
Hand-made LODs are separate objects named <model>_LOD0, <model>_LOD1, ... sharing one pivot.
"""

import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lh_export  # noqa: E402

stem = os.path.splitext(os.path.basename(bpy.data.filepath))[0]
if not stem:
    sys.exit("Open a saved .blend file (blender --background <file.blend> --python ...)")
glb, _ = lh_export.output_paths(stem)
lh_export.export_glb(glb)
