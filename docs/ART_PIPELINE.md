# Art pipeline — Blender → glTF → Godot

How a model gets from Blender into Love & Honor. Design references: §28.1 (style), §28.1a (pipeline, kit, budgets),
§28.2 (palette), §28.3 (Georgian kit), §30.6 (repo layout). Status: **Phase 0 prep** — the pipeline, rules and checks
exist and one placeholder kit piece has gone through them end to end. There is no final art yet.

Every rule with a number lives in [`data/art_pipeline.json`](../data/art_pipeline.json) (the numbers below are
copies for reading; the JSON wins). Numbers not given by the design doc are `[TWEAK]` guesses.

## 1. The short version

1. Model in Blender at real-world scale (1 unit = 1 m), Z up, the model's front facing **−Y**.
2. Name the file and the objects by the rules in §3. Hand-made LODs are separate objects `<model>_LOD0`, `_LOD1`, …
3. Use only pipeline materials: `pal_<brand colour>` or `mat_<special>` (§5).
4. Put the pivot on the ground (§4). Apply scale. Turn on Backface Culling.
5. Export with the standard script — never by hand from the menu:
   ```powershell
   & "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --background art\blend\kit\georgian\<model>.blend --python tools\blender\export_blend.py
   ```
6. Run the checks: `dotnet run --project tools/DataValidator` (also runs in `dotnet test`).
7. Godot imports it on the next editor start / `--import`; the default post-import script sets up LODs (§6).
8. The game assigns materials at runtime from the palette (§5), so colours follow `data/branding.json`.

## 2. Folders

```
art/blend/                 .blend sources (Git LFS; .gdignore so Godot never imports them)
  kit/georgian/            modular Georgian Revival kit (§28.1a)
  buildings/  landmarks/  characters/  props/  trees/
assets/models/             exported .glb only (Git LFS) — same sub-folders as art/blend/
tools/blender/             Blender Python: lh_export.py (shared helpers), export_blend.py, make_example_kit.py
scripts/import/            lh_post_import.gd (Godot post-import script, the project default for 3D scenes)
scripts/bridge/            PaletteMaterials.cs (runtime material hookup), ArtImportTestHost.cs (check scene)
scenes/spikes/art_import_test.tscn   the import check scene (boot menu → "Step 4 — Art import check")
```

Only `.glb` files go in `assets/models/`. The checker rejects `.gltf`, `.blend`, `.fbx` and `.obj` there. Godot
could import `.blend` files directly, but that needs Blender on every machine and hides the export settings.
Blender's `*.blend1` backups are ignored by git.

## 3. Naming

**Files:** `<category>_<name>[_<variant>].glb`, all lowercase `snake_case`, digits allowed (`3m`, `1m5` = 1.5 m).

| Prefix | Folder | What | LOD0 budget | LOD levels | Variants |
|---|---|---|---|---|---|
| `kit_` | `kit/<set>/` | one modular kit piece; the 2nd token is the set (`kit_georgian_…`) | ≤ 800 (ceiling; targets in §8) | 1–3 | — |
| `bldg_` | `buildings/` | a whole ordinary building | 1–5k (≤ 5,000) | 1–4 | `construction`, `new`, `weathered`, `renovated` |
| `hero_` | `landmarks/` | hand-modelled landmark (Upham arch, King Library, the Seal, …) | ≤ 15,000 | 1–4 | same as bldg |
| `char_` | `characters/` | person figure | ≤ 500 | **3–4** | — (era clothing = separate files) |
| `prop_` | `props/` | lamp posts, benches, bike racks, signs | ≤ 300 | 1–3 | — |
| `tree_` | `trees/` | trees and shrubs | ≤ 600 | 1–4 | `spring`, `summer`, `fall`, `winter` |

The bldg, hero and char budgets are from §28.1a. The kit, prop and tree budgets are `[TWEAK]` guesses. A variant
word that belongs to another category (e.g. `kit_…_weathered`) is rejected.

Examples: `kit_georgian_wall_window_3m`, `kit_georgian_cupola_large`, `bldg_generic_hall_3storey`,
`hero_upham_arch`, `char_student_2020s`, `prop_lamp_iron`, `tree_oak_fall`.

**Objects (become Godot nodes):** lowercase `snake_case`, with an optional `_LOD<n>` suffix (the only uppercase
allowed). If a model has hand-made LODs, every mesh object carries a suffix and the levels run `_LOD0`, `_LOD1`, …
with no gaps. Objects without a suffix are drawn at every distance.

**Materials:** see §5.

## 4. Blender conventions

- **Units:** Metric, Unit Scale 1.0. 1 Blender unit = 1 m = 1 Godot unit. The tile grid is 10 m (§5.1).
- **Axes:** Blender is Z-up; the exporter converts to Godot's Y-up. Model the **front facing −Y** (Blender's Front
  view looks at it); it faces **+Z** in Godot.
- **Pivot (origin):** on the ground (**z = 0**, the model's lowest point). The checker fails a model whose lowest
  point is more than 5 cm off 0.
  - Kit pieces: the **bottom-left corner of the outer wall face**, so pieces tile by adding the module width
    along +X. Wall thickness goes into +Y (inside the building).
  - Buildings, landmarks, props, trees and characters: the centre of the footprint.
- **Kit grid:** module width **3.0 m** (one window bay), storey height **3.5 m** (equal to
  `rendering.json extrusion.level_height_m`, and the validator keeps them equal), wall thickness **0.4 m**, snap
  **0.5 m**.
- **Transforms:** apply scale (Ctrl+A → Scale). Rotation is fine. The checker fails any node with unapplied scale.
- **Modifiers** (mirror, array, bevel …) are fine; the export applies them.
- **Shading:** flat (faceted, §28.1). No normal maps (tangents aren't exported). No UV unwrapping needed unless the
  piece uses the brick pattern (§5).
- **Faces nobody sees** (bottoms, the backs of walls against other walls, the insides of closed boxes): delete them
  in final art. They count against the budget. The Phase 0 placeholder keeps closed boxes for simplicity.
- **Backface Culling ON** for every material (exported as glTF `doubleSided = false`). Only `tree_` models may
  have double-sided materials (leaf cards).
- **Custom properties** (Object Properties → Custom Properties) become glTF extras and then Godot node metadata.
  Use the `lh_` prefix (e.g. `lh_category`).

## 5. Materials and colour

Colour comes from data, not from the model:

| Material name | Colour source | Example |
|---|---|---|
| `pal_<key>` / `pal_<key>_<n>` | `data/branding.json` → `colors.<key>` (entry n of a list) | `pal_brick_1`, `pal_trim`, `pal_slate`, `pal_copper`, `pal_limestone`, `pal_primary` |
| `mat_<name>` | `data/art_pipeline.json` → `special_materials` | `mat_glass`, `mat_iron`, `mat_window_glow` |

- In Blender, `tools/blender/lh_export.py` → `palette_material(name)` creates these materials with a preview colour
  (sRGB → linear conversion done for you).
- In Godot, `PaletteMaterials.Apply(node)` replaces each imported material with **one shared material per name**.
  Colours then follow the active branding profile (§36.1 swappable) with no re-import. Shared materials also keep
  batching possible.
- The checker fails a material outside these rules, or a palette index the branding file doesn't have. A unit test
  also checks every model against the stand-in branding profile, so swapping profiles can't break a model.
- **Brick pattern** (§28.1: "a subtle brick pattern on walls is the main exception"): planned as a world-space
  shader on `pal_brick_*` (no UVs needed, works on the Mobile renderer), not as a texture. Not built yet.
- **Night window glow** (§28.1): `mat_window_glow` is emissive; the plan is to swap glass to glow by time of day.
- **Mobile renderer (iPad later, Locked Decision):** opaque materials only (glass is opaque dark blue-grey), no
  normal maps, few unique materials, StandardMaterial3D or simple custom shaders. Transparency and
  screen-space refraction are off-limits for buildings.

## 6. Godot import

- **Defaults for every 3D model** (`project.godot [importer_defaults] scene`):
  - Post-import script `res://scripts/import/lh_post_import.gd`.
  - `meshes/ensure_tangents = false` (no normal maps).
  - Engine defaults kept: automatic mesh LOD generation (`meshes/generate_lods`) and shadow meshes.
- **The post-import script** runs on every import:
  - It reads the category from the file-name prefix.
  - For each `_LOD<n>` node it sets the visibility range from the category's `lod_distances_m`, plus a
    hysteresis margin (`lod.range_margin_m`) so LODs don't flicker at the switch distance. LOD0 is visible from
    0 to d₀, LOD1 from d₀ to d₁, and so on; the last LOD has no end distance.
  - It stamps `lh_category` on the scene root and `lh_lod` on each LOD node.
  - It leaves materials as imported; the swap happens at runtime (§5).
- **Two LOD layers stack:**
  - Hand-made LODs (visibility ranges) change the *shape* at distance.
  - Godot's automatic mesh LOD simplifies each mesh further by screen size, for free.
- **Distances** (`[TWEAK]`):
  - kit: 60 / 180 m.
  - bldg: 150 / 400 / 900 m.
  - hero: 200 / 500 / 1,100 m.
  - char: 30 / 80 / 200 m.
  - prop: 50 / 150 m.
  - tree: 80 / 250 / 600 m.
  - The camera ranges from 25 to 3,500 m (`rendering.json camera`).
- **Instancing:**
  - Props, trees and characters are drawn with `MultiMeshInstance3D`, as the Spike A walkers are.
  - Kit pieces are not placed as thousands of nodes. A building's pieces get merged into one mesh per
    building (or per terrain chunk, like the Spike B footprints) when the building is placed.

## 7. Checks

`dotnet run --project tools/DataValidator` validates `/data` and then every file in `assets/models/`. The same
checks run in `dotnet test` (`ModelValidationTests`). Per model:

| Check | Fails when |
|---|---|
| Git LFS | the file is an LFS pointer (run `git lfs pull`) |
| Format | not a glTF 2.0 binary |
| Name | not `<category>_<name>` snake_case, unknown prefix, or wrong folder |
| Variant | the last token is a variant word not allowed for the category |
| Budget | LOD0 triangles over `max_tris` |
| LODs | gaps in `_LOD` numbering, level count outside the category's range, LOD n over `max_ratio_to_lod0[n]` × LOD0 (100 / 50 / 25 / 12 %) |
| Materials | not `pal_*` / `mat_*`, palette index missing from branding.json, faces without a material, double-sided outside `tree_` |
| Transforms | a node with unapplied scale |
| Pivot | lowest point more than 5 cm from y = 0 |

Limits of the checker:
- It reads only the glTF JSON chunk.
- Triangle counts are exact.
- The pivot check ignores parent/child transforms and rotations, so it is exact for flat hierarchies, which is
  what the export produces.
- It can't judge whether a piece *looks* right; that's what the import check scene is for.

## 8. The Georgian Revival kit (§28.1a, §28.3) — planned pieces

Most historic buildings are assembled from this kit (§28.1a).
- **Sizes** are width × height on the 3 m / 3.5 m grid.
- **Targets** are per-piece LOD0 goals (`[TWEAK]`); the enforced ceiling is 800.
- **Status:** one placeholder piece exists; nothing else is modelled yet.

| Group | Pieces (file names) | Size | Target tris |
|---|---|---|---|
| Walls (brick) | `kit_georgian_wall_plain_3m`, `_wall_plain_1m5` (filler), `_wall_opening_window_3m`, `_wall_opening_door_3m`, `_wall_base_3m` (water table), `_wall_gable_3m` | 3 × 3.5 (1.5 filler) | 12–40 |
| Corners | `kit_georgian_corner_out`, `_corner_in`, `_corner_quoin_out` (limestone quoins) | 0.4 × 3.5 | 12–80 |
| Windows (white trim, multi-pane) | `kit_georgian_window_6over6`, `_window_9over9`, `_window_arched`, `_window_palladian_3m`, `_window_oculus` | 1.2 × 2.0 (Palladian 3 × 2.8) | 20–120 |
| Doors | `kit_georgian_door_fanlight_single`, `_door_fanlight_double` | 1.2–2 × 2.9 | 60–150 |
| Porticos & columns | `kit_georgian_column_doric`, `_column_ionic`, `_entablature_3m`, `_pediment_3bay`, `_portico_steps_3m` | column 0.5 Ø × 7 | 40–200 |
| Roofs (slate) | `kit_georgian_roof_hip_straight_3m`, `_roof_hip_end`, `_roof_hip_corner_out`, `_roof_hip_corner_in`, `_roof_gable_straight_3m` | 3 m sections | 8–30 |
| Dormers | `kit_georgian_dormer_gable`, `_dormer_arched` | 1.5 × 2 | 60–150 |
| Cupolas | `kit_georgian_cupola_small`, `_cupola_medium`, `_cupola_large` (with clock faces), in `pal_copper` or `pal_trim` | 2 / 3.5 / 5 m wide | 200 / 400 / 800 |
| Clock faces | `kit_georgian_clock_face` (hands as 2 extra quads for animation) | 1 m Ø | 20–40 |
| Chimneys | `kit_georgian_chimney_single`, `_chimney_double` | 0.8–1.6 × 2 | 12–40 |
| Cornices & courses | `kit_georgian_cornice_3m`, `_cornice_corner`, `_beltcourse_3m` (limestone) | 3 m | 8–30 |

Also from §28.3, as props rather than kit: `prop_lamp_iron` (black iron lamp post), `prop_bench_white`.
Herringbone brick paths belong to the terrain/path shader, not to models.

**Planned change to the example piece:** its window becomes a separate `_wall_opening_window_3m` plus a
window insert (panes drawn by the window shader, §10), so window styles vary without multiplying wall pieces. It is a single combined piece for now
because its only job is to exercise the pipeline.

## 9. The example: `kit_georgian_wall_window_3m` (placeholder)

Built by a script so anyone can regenerate it:

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --background --factory-startup --python tools\blender\make_example_kit.py
```

It writes `assets/models/kit/georgian/kit_georgian_wall_window_3m.glb` and `art/blend/kit/georgian/…blend`.
- **What it is:** one 3 m × 3.5 m brick bay with a 6-over-6 sash window, white trim, a limestone sill and a
  flat-arch lintel.
- **LODs:** LOD0 182 tris; LOD1 64 tris (flat window panel); LOD2 14 tris (slab with a painted window).
- **Proportions:** placeholders, not measured from any real building.

Check it in Godot: boot menu → **Step 4 — Art import check**, or run
`& $env:GODOT --path . scenes/spikes/art_import_test.tscn`.
- The scene tiles the piece into a 6-bay, two-storey facade (checks the pivot and module size) and shows each LOD
  side by side.
- The overlay lists triangles, LOD distances, how many materials were assigned, and which LOD the facade shows at
  the current camera distance. Zoom out past 60 m and 180 m to see it switch.
- Screenshots: `docs/images/art-import-kit-piece.png`, `docs/images/art-import-lod1.png`.

## 10. Decisions and findings

Decided by the user on 2026-09-30. None of this is built yet: it applies from Phase 1. Each one goes into the
Phase 0 report as a suggested GAME_DESIGN.md update (§28.1a, §30).

1. **Window panes are drawn by a shader, not geometry. (Decided.)**
   - Why: a typical 3-storey, 11-bay hall has about 90 window bays around it. §28.1a allows 1–5k triangles per
     building, which leaves about 40–55 tris per bay, roof included. The example bay is 182 tris at LOD0 because
     its muntins and frame are boxes; a hall built from it would be about 16k tris.
   - Plan: a small shader on the glass quad draws the panes and muntins as a procedural grid (no texture,
     Mobile-safe). Pane counts (6-over-6, 9-over-9 …) are shader parameters.
   - The frame and sill stay as a few faces, and hidden faces are deleted, which gets a bay to about 30–40 tris.
   - The example piece keeps geometry muntins until the shader exists; it only exercises the pipeline.
2. **Real buildings are built by a procedural kit assembler. (Decided.)**
   - Why: OSM outlines don't follow a 3 m grid, so a snap-together kit can't wrap them by hand at the scale of
     180+ timeline buildings.
   - Plan: the assembler places wall bays along each footprint edge (stretching them or filling the remainder),
     puts corners at the vertices, and generates hip roofs with a straight skeleton. It is driven by a
     per-building "recipe" in data: storeys, bay rhythm, portico, cupola, era.
   - Hand-modelled `hero_` models stay for the §28.1a landmarks.
   - The same assembler builds player-placed buildings.
   - Kit pieces must be authored so they can be stretched a little along X (no detail within 0.25 m of a
     bay's side edges).
3. **Era variants are mostly shader parameters. (Decided.)**
   - Plan: new and weathered are shader settings (tint, grime); construction-stage is scaffolding props plus the
     height-clip idea already in `buildings.gdshader`.
   - Separate `_renovated` models only where a renovation really changed the shape. The `construction`,
     `new` and `weathered` file variants stay allowed in `art_pipeline.json` for exceptions.
4. **Rendered characters use vertex-animation textures on a MultiMesh. (Decided.)**
   - Why: 1,500–3,000 animated figures (§30.2) can't each have their own skeleton.
   - Plan: the animation is baked into a texture and played in the shader.
   - `char_` models are authored for this: one mesh, a shared rig, and a few short loops (§28.1a: walk, idle,
     sit, carry).
5. **Renderer: noted.** `project.godot` uses Forward+. Everything here should be Mobile-compatible, but it has to
   be tested on the Mobile renderer before any iPad work.
6. **LOD distances are guesses (open).** Tune them with real art in the terrain scene, where the camera goes to
   3.5 km. Past about 1 km, buildings should probably become merged chunk blocks, like today's extruded
   footprints.
