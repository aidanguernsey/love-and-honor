"""Shared Blender helpers for the Love & Honor art pipeline (docs/ART_PIPELINE.md).

Runs inside Blender's Python (bpy). Scripts in this folder add it to sys.path themselves.

- repo_root() / load_data(name): find the repo (walks up to project.godot) and read /data JSON.
- palette_material(name): create/reuse a pal_* or mat_* material, previewed in the branding colour.
- export_glb(path): the one standard glTF export (GLB, +Y up, modifiers applied, no cameras/lights).
- output_paths(stem): where a model's .glb and .blend go, from its name prefix and art_pipeline.json.
"""

import json
import os
import re

import bpy

NAME_RE = re.compile(r"^[a-z][a-z0-9]*(_[a-z0-9]+)+$")


def repo_root() -> str:
    d = os.path.dirname(os.path.abspath(__file__))
    while True:
        if os.path.exists(os.path.join(d, "project.godot")):
            return d
        parent = os.path.dirname(d)
        if parent == d:
            raise RuntimeError("project.godot not found above " + __file__)
        d = parent


def load_data(name: str) -> dict:
    with open(os.path.join(repo_root(), "data", name), encoding="utf-8") as f:
        return json.load(f)


def output_paths(stem: str) -> tuple[str, str]:
    """(glb path, blend path) for a model named like kit_georgian_wall_window_3m."""
    if not NAME_RE.match(stem):
        raise ValueError(f"'{stem}': model names are lowercase snake_case, e.g. kit_georgian_wall_window_3m")
    categories = load_data("art_pipeline.json")["categories"]
    prefix, group = stem.split("_")[0], stem.split("_")[1]
    if prefix not in categories:
        raise ValueError(f"'{stem}': unknown category prefix '{prefix}' (art_pipeline.json: {', '.join(categories)})")
    folder = categories[prefix]["folder"]
    # Kit pieces are grouped by style set (kit_georgian_* -> kit/georgian/); other categories are flat.
    sub = [folder, group] if prefix == "kit" else [folder]
    root = repo_root()
    return (os.path.join(root, "assets", "models", *sub, stem + ".glb"),
            os.path.join(root, "art", "blend", *sub, stem + ".blend"))


def _srgb_to_linear(c: float) -> float:
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def _hex_to_linear(hex_color: str) -> tuple[float, float, float, float]:
    v = int(hex_color[1:], 16)
    return (_srgb_to_linear(((v >> 16) & 255) / 255), _srgb_to_linear(((v >> 8) & 255) / 255),
            _srgb_to_linear((v & 255) / 255), 1.0)


def palette_material(name: str) -> bpy.types.Material:
    """Material named by the pipeline rule; its Blender colour is only a preview (the game assigns the real one)."""
    mat = bpy.data.materials.get(name)
    if mat:
        return mat
    art = load_data("art_pipeline.json")
    m = re.match(r"^pal_([a-z]+)(?:_(\d+))?$", name)
    if m:
        colors = load_data("branding.json")["colors"]
        values = colors[m.group(1)] if isinstance(colors[m.group(1)], list) else [colors[m.group(1)]]
        hex_color, roughness, metallic, emission = values[int(m.group(2) or 0)], 0.85, 0.0, 0.0
    elif name in art["special_materials"]:
        s = art["special_materials"][name]
        hex_color, roughness, metallic, emission = s["color"], s["roughness"], s["metallic"], s["emission_energy"]
    else:
        raise ValueError(f"material '{name}' must be pal_<branding colour>[_<n>] or a special_materials entry")
    mat = bpy.data.materials.new(name)
    bsdf = mat.node_tree.nodes.get("Principled BSDF") if mat.node_tree else None
    if bsdf is None:
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes["Principled BSDF"]
    color = _hex_to_linear(hex_color)
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if emission > 0:
        bsdf.inputs["Emission Color"].default_value = color
        bsdf.inputs["Emission Strength"].default_value = emission
    mat.diffuse_color = color  # solid-mode viewport colour
    mat.use_backface_culling = True  # exported as glTF doubleSided=false (see allow_double_sided)
    return mat


def export_glb(path: str) -> None:
    """The standard export. Every model in assets/models/ goes through this so settings never drift."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        export_yup=True,          # Blender Z-up -> Godot Y-up
        export_apply=True,        # apply modifiers (mirror, array, bevel ...)
        export_normals=True,
        export_texcoords=True,
        export_tangents=False,    # no normal maps in the low-poly style
        export_materials="EXPORT",
        export_cameras=False,
        export_lights=False,
        export_extras=True,       # custom properties -> glTF extras -> Godot node metadata
        use_selection=False,
        use_visible=False,
    )
    print(f"LH_EXPORT {path}")
