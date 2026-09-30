# Terrain approach: custom mesh generator vs. Terrain3D (§30.3, open question §38.1)

**Recommendation: use the custom mesh generator.** Revisit Terrain3D only if close-zoom texture splatting,
foliage painting or in-editor sculpting become requirements.

Tested 2026-09-30 on the real Oxford map (4 km × 4 km, 1 m USGS lidar resampled to 5 m, 400 × 400 tiles),
Godot 4.7.2 .NET, Forward+, 1600 × 900, RTX 4070 Laptop GPU, same sun, sky and camera positions for both.

## What was tried

| | Custom generator (`scripts/sim/View/TerrainMesher.cs`) | Terrain3D v1.0.2-stable (GDExtension) |
|---|---|---|
| Setup | 100 chunks of 40 × 40 tiles, 1 quad per 10 m tile, 3 LODs (1/2/4 tiles per quad) via visibility ranges, skirts | 801² heightmap imported at 5 m vertex spacing (16 regions of 256²), default clipmap (7 LODs) |
| Colours | Per-tile vertex colours from the tile data layer | Tile colours as Terrain3D colour map, shown with `show_colormap` (no texture assets) |
| Trial location | In the repo (`scenes/spikes/terrain_spike.tscn`) | Throw-away scratch project; **nothing from Terrain3D was added to the repo** |

The Terrain3D trial was deliberately minimal: default mesh settings, no textures, no shader override. Its colour
map came from the pipeline preview image, so its palette differs slightly; compare shading and crispness, not hue.

## Measurements (GPU/CPU = Godot's measured render time per frame; FPS is capped at ~120 by this laptop's display path and isn't meaningful)

| View (`--camera=`) | Custom: GPU ms · render CPU ms · triangles · draw calls | Terrain3D: GPU ms · render CPU ms · triangles · draw calls |
|---|---|---|
| Whole map, far (`2000,2150,3400,55,0`) | **0.51** · 0.50 · 37 k · 102 | 1.25 · 0.24 · 253 k · 36 |
| Creek valley, low (`3150,2600,520,22,70`) | **1.10** · 0.39 · 258 k · 169 | 1.85 · 0.62 · 677 k · 108 |
| Campus core (`2150,2050,420,48,15`) | **1.40** · 0.60 · 305 k · 181 | 1.77 · 0.40 · 631 k · 111 |
| Mid-distance (`2000,2000,900,25,0`) | 1.48 · 0.66 · 250 k · 163 | **1.44** · 0.39 · 627 k · 102 |
| Video memory (whole scene) | 183 MB | **150 MB** |
| Load / build | 110 ms load + 470 ms mesh build | 335 ms import |

Both are far inside a 16.7 ms (60 FPS) frame. Performance does not decide this.

## Comparison

| Criterion | Custom generator | Terrain3D |
|---|---|---|
| **Low-poly look (§28.1, locked)** | Native: per-face normals, facets fixed in world space, 1 quad = 1 tile | Smooth per-pixel normals. Faceting needs a shader override, and its camera-centred clipmap geometry would make facets shift as the camera moves |
| **Tile grid & data layer (§30.3)** | Mesh aligned to tiles; grid, hover and per-tile colours are trivial (shader + vertex colours) | Colour map is filtered (blurry up close); grid/hover would need a custom shader; picking via its API |
| **Evolving map (§5.1b)** | Land-state change → rebuild the affected chunk's 3 LODs (a few ms) | Colour/control map edits at runtime are supported |
| **Mobile / iPad later** | Plain meshes: works with Mobile/Compatibility renderers | Ships iOS/Android binaries; clipmap shader cost on mobile untested |
| **Dependency risk** | None; ~150 lines of tested C# | Native binaries per platform; officially supports Godot 4.4–4.6 (it did load and run on 4.7.2); upgrades wait on its releases |
| **Features we'd get for free** | — | Sculpt/paint editor tools, collision, foliage instancer, navigation baking, holes, huge worlds |
| **Do we need those?** | Terrain comes from real data (no sculpting); map is a fixed 4 km; picking is done by raycasting the heightmap in C#; trees will use our own MultiMesh (§30.3) | Mostly not |
| **Weak spots** | More draw calls (up to ~180; could merge LOD chunks later); LOD switches pop (no geomorphing), mitigated by skirts and distance thresholds | More triangles drawn at every view; look fights the art direction |

## Follow-ups for the custom generator (Phase 1+)
- Rebuild single chunks when land states change (land clearing, construction) instead of the whole terrain.
- Consider merging far LOD chunks to cut draw calls, and tune `lod_switch_m` against visible popping.
- Replace flat per-tile colours with a small palette texture + seasonal tint (3c) and roads/paths as their own meshes.

Screenshots: `docs/images/terrain-custom-*.png`, `docs/images/terrain-terrain3d-*.png`.
