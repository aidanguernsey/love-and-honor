# CLAUDE.md — Love & Honor

A low-poly 3D university city-builder set at Miami University (Oxford, Ohio), 1809 → present → future.
Godot 4 (.NET build) + C#. Non-commercial fan / portfolio project.
The full design is in `GAME_DESIGN.md` (v0.4). **Read the relevant section before working on a system.**

## Current phase
**Phase 0 complete (2026-09-30).** All 16 design-doc updates approved and applied (GAME_DESIGN.md v0.4).
**Next: Phase 1 — "The Hill"**, checkpoints 1a–1k in `docs/PHASE0_REPORT.md` §5 (approved), starting with **1a**
(sim on the real map + background flow-field rebuilds). Stop after every checkpoint.
**1a done (2026-10-01):** sim on the real Oxford map (84 Miami buildings + 108 housing zones), incremental background
flow-field updates swapped in at a fixed tick (`docs/benchmarks/phase1-1a-real-map-2026-10-01.txt`).
**1b done (2026-10-01), awaiting the user's review:** walking model v2 (§12.4 v0.4) — classes start on the hour and
people leave early (late only after a back-to-back class or a walk that ended too late: 134k late walks vs 1.12M);
lawn 1.25x + 15 m path-exit penalty (big quads get cut, small lawns/corners don't); rough ground 3x (fields, woods AND
town yards: only university land counts as lawn); desire paths from >=40 walkers/day, 14 days to wear, 30 to regrow;
free time chosen per 2-hour block. Real map: 98.6% of crossings on paths, 259 worn tiles after 48 days
(`docs/images/real-map-desire-paths.png`). Benchmark `docs/benchmarks/phase1-1b-walking-2026-10-01.txt`: gated p95
6.1 ms, paced 1x 6.7 ms (machine ~1 ms slower today than for 1a: the 1a code measured 6.0 ms). Next: **1c** (game shell).

### Phase 0 record — Foundations & technical spikes (§37)
Steps: (1) scaffold → (2) Spike A population at scale → (3) Spike B Oxford terrain + timeline →
(4) art pipeline prep → (5) outreach drafts → (6) Phase 0 report.
Status: Step 1 done. Step 2 (Spike A) done: 2a headless sim + benchmark — PASS, p95 ≈ 3.5–4.0 ms on 4 E-cores
(`docs/benchmarks/spike-a-2026-09-30.txt`); 2b rendering scene (`scenes/spikes/population_spike.tscn`) — 2,000
walkers, ~118 FPS (V-Sync cap), 0 dropped ticks at 8×. Screenshots in `docs/images/`.
Step 3a (map pipeline) done: real Oxford data in `data/map/` (1 m USGS lidar → 5 m heightmap, OSM layers
rasterized to the 400×400 tile grid). Step 3b done: real-map tile data layer (`RealMapLoader`), custom low-poly
terrain (`TerrainMesher`, `scenes/spikes/terrain_spike.tscn`), build grid + tile inspector; Terrain3D trialled in a
scratch project → recommend the custom generator (`docs/TERRAIN_COMPARISON.md`).
Building-dates research done: `data/timeline.json` has 177 Miami buildings/landmarks (current + past) with cited sources,
confidence levels and OSM links; report in `docs/research/BUILDING_DATES.md`.
Step 3c done: timeline 1809 → 2026 in the Spike B scene (land states + roads repaint per year, buildings shown/hidden
by year in the shader, seasons, sun for Oxford). Screenshots `docs/images/timeline-*.png`.
Step 4 done (prep only, no final art): `docs/ART_PIPELINE.md`, rules in `data/art_pipeline.json`, Blender scripts in
`tools/blender/`, post-import LOD script, runtime palette materials, model checks in the validator, one placeholder kit
piece (`kit_georgian_wall_window_3m`) imported and shown in `scenes/spikes/art_import_test.tscn`.
Step 5 done: outreach drafts in `docs/outreach/` (trademark/licensing, Myaamia Center + summary attachment, Uptown
courtesy note, README with a send log). **Nothing sent.**
Step 6 done: `docs/PHASE0_REPORT.md` (benchmarks, terrain, risks, Phase 1 breakdown 1a–1k, 16 suggested design-doc
updates U1–U16). The user approved all 16 and they are applied in GAME_DESIGN.md v0.4.

### Findings to carry into the Phase 0 report
- Flow-field rebuild: ~1.4 s on 4 E-cores / ~0.3 s on 4 P-cores for 41 buildings (Dijkstra per building). The
  real game has more buildings → must rebuild asynchronously (double-buffer fields) and/or use a faster algorithm.
- Tick scaling: 4 P-cores ≈ 3.1× one P-core; 4 E-cores only ≈ 1.25× one E-core (shared-cluster limits).
- Occasional single-tick max spikes (6-11 ms) from OS preemption; p99 stays ≈ 4 ms.
- Lateness: agents depart on the hour, so any walk > class-change window (10 min) counts as late (avg walk
  11.9 min on the spike map). Phase 1 needs "leave before the hour" departures.
- Free-hour activity is re-rolled every hour → ~14.8k walks/tick (stress case); smooth it in Phase 1.
- Map data: elevation 231.6–296.8 m (65 m relief). OSM: 3,138 buildings, only 1 with a start_date → historic
  timeline can't come from OSM. ~26% of tiles have no land-use tag ("open"). Overpass servers are often busy (504):
  pipeline retries + falls back to a second server. `out geom tags` silently drops relation members (bug found & fixed).
- OSM sidewalks/crossings are left out of the path raster (they doubled every street on 10 m tiles); campus is still
  path-dense because Miami really is. 2.5 m paths on 10 m tiles is coarse: paths should become their own meshes.
- Land-state list gained 'water' (not in §5.1b) so creeks/ponds are unbuildable — flag for the design doc. §5.1b mixes
  physical state with ownership (town-owned / university-owned); implemented as listed plus a separate Ownership layer.
- timeline.json format: `sources` table + entries with built_year/built_precision/demolished_year/status
  (standing|demolished|incorporated|moved|unknown)/confidence (corroborated|sourced|conflicting|estimate|unknown)/
  verified(false)/osm_id. Conflicts: Miami's own timeline wins unless a stated reason says otherwise (in notes).
  Demolished-in-2026 buildings (Wells, Williams, Hanna, Joyner) still have OSM outlines and are linked to them.
  Never mark verified:true without University Archives confirmation.
- Timeline perf: repaint 5–11 ms per year change (main thread); GPU 1.1–1.6 ms; 3,143 footprints = 66k tris.
  45 timeline entries have no footprint (gates, bridges, unknown sites) and aren't drawn.
- Terrain: custom GPU 0.5–1.5 ms/frame vs Terrain3D 1.2–1.9 ms; Terrain3D officially supports Godot 4.4–4.6 but ran on 4.7.2.
- Rendered walkers: cosmetic sample of the latest tick's real walks, limited to a detail radius around the
  look-at point (at low tilt the view reaches km away; far walkers are sub-pixel → impostors later, §30.2).
- Instance colours need `VertexColorIsSrgb = true` on the material, or palette colours render washed out.
- Art (Step 4, details in `docs/ART_PIPELINE.md` §10): a Georgian hall has ~90 window bays, so §28.1a's 1–5k tris
  leaves ~40–55 tris/bay; geometry muntins make the example bay 182 tris (a hall ≈ 16k) → draw panes/muntins in a
  shader. Real OSM footprints aren't on a 3 m grid → recommend a procedural kit assembler + per-building recipes
  (hero landmarks hand-made). Era variants ×4 → mostly shader parameters. Rendered characters need vertex-animation
  textures on a MultiMesh. Project still uses Forward+; nothing tested on the Mobile renderer yet.
- Phase 1 1a/1b: idle cores start a tick slower, so ticks paced like the game at 1x are ~0.5-1 ms slower than
  back-to-back ones (both reported). The real map's agent phase costs more than the synthetic one (2.9 vs 2.1 ms on
  E-cores): the 192x192 distance table misses cache (synthetic: 41x41) — a smaller (ushort minutes) table is an option.
  Students who walk home for a few minutes before a class can still be late (chained walks); acceptable for now.
- Hotkeys: §27.5 says "1–5 speed" but there are 5 speed states incl. pause; implemented Space = pause,
  1–4 = 1×/2×/4×/8× (5 reserved for skip-to-next-event, §6.1). Flag for the user.

### Decisions made during Phase 0 (by the user)
- Spike A pass criterion: **p95 tick time ≤ 8 ms** over 1,000 ticks (avg and max reported too). (2026-09-30)
- Spike A split into 2a / 2b checkpoints. (2026-09-30)
- Spike B split into 3a (Python map pipeline) / 3b (terrain + tile layer + Terrain3D comparison) / 3c (timeline,
  look & feel). Downloads approved: PyPI packages into `tools/.venv`, USGS 3DEP + OSM data, Terrain3D plugin trial.
  Heightmap stored at 5 m; terrain mesh at 10 m (tile size). (2026-09-30)
- **Terrain approach: custom mesh generator** (not Terrain3D) — resolves §38 open question 1. Record as a suggested
  GAME_DESIGN.md update (§30.3, §38) in the Phase 0 report. (2026-09-30)
- Before 3c: research construction/demolition dates for all current and past Miami (Oxford) campus buildings into
  `data/timeline.json`, every entry sourced and still `verified: false`. (2026-09-30)
- Proposed, not objected to: sim resolves walks within the hourly tick (depart/arrive minutes); rendered
  walkers use a cosmetic walk speed decoupled from game time. Spike campus adds off-campus housing blocks
  and doesn't enforce capacity. (Record as a suggested GAME_DESIGN.md update in the Phase 0 report.)
- Step 4 scope: **prep only** (no §37 art spike yet). (2026-09-30)
- Art decisions after Step 4 (2026-09-30), all to be recorded as suggested GAME_DESIGN.md updates (§28.1a, §30):
  1. Window panes/muntins are drawn by a **shader** on the glass quad (not geometry), so halls fit 1–5k tris.
  2. Real buildings are built by a **procedural kit assembler** from footprints + per-building recipes (storeys,
     bay rhythm, portico, cupola, era); hero landmarks stay hand-modelled; player buildings use the same assembler.
  3. Era variants (new / weathered / construction) are mostly **shader parameters** (+ scaffolding props); separate
     `_renovated` models only where the shape really changed.
  4. Rendered characters: **vertex-animation textures on a MultiMesh** (one mesh, shared rig, short loops).
  5. Mobile renderer compatibility: noted; test on the Mobile renderer before any iPad work.
- Walking & desire paths (user, current Miami student, 2026-09-30): most people stay on paved paths and cut across
  grass only when it clearly saves time; only regularly walked shortcuts get matted down to dirt. **Keep desire paths**,
  but they should be less common than now. Phase 1 model: (1) paths strongly preferred — grass cost multiplier plus
  a one-off "step off the path" penalty, so small corner cuts aren't worth it but long diagonals (Slant Walk) still
  are; (2) wear from *sustained* traffic with grass regrowth (recent-traffic decay), not all-time totals, so only
  regular shortcuts turn to dirt ("over weeks", §12.4). Spike A's 1.3× grass cost + cumulative wear overstate grass
  use (its synthetic map also removes path segments on purpose). Suggested §12.4 update ("Students walk shortest
  routes" → "prefer paved paths; cut across grass when it saves enough") goes in the Phase 0 report.
- Phase 0 wrap-up (2026-09-30): **all 16 design-doc updates (U1–U16) approved** → applied as GAME_DESIGN.md v0.4.
  Carried-over defaults (report §7.2) stand. **Campaign Chapter 1 starts in 1824** (map derived for that year; the
  sandbox "Empty Hill" covers 1809). Phase 1 checkpoints 1a–1k and the proposed cuts approved. Building-dates
  generator kept in `tools/research/` (reproduces `data/timeline.json`; timeline is hand-edited from now on).
  Housekeeping: add a private remote and push (gh CLI not installed; needs the user to create the repo / sign in).

## Working rules
- Work in small steps. At the end of each step: stop, summarize, explain how to verify, and wait for the user's go-ahead.
- Commit after each working step with a clear message.
- **Edit `GAME_DESIGN.md` only to apply changes the user has explicitly approved.** Propose design-doc changes as a list first.
- **Locked Decisions (below) are final.** Don't change them without asking.
- If the design is ambiguous or technically unrealistic, flag it. Never silently change the design.
- Real-world facts (dates, names, figures) are marked `[VERIFY]` in the design doc. In data files, mark every
  unverified fact with `"verified": false` and never present a guess as fact.
- Outreach emails are drafts only (`docs/outreach/`). Never send anything.

## Locked decisions (summary of the table in GAME_DESIGN.md)
| Topic | Decision |
|---|---|
| Title | Love & Honor |
| Branding | Real Miami University names/logos/marks, permission pending (§36). **All branding must be swappable via `/data/branding.json`.** |
| Art | Low-poly 3D |
| Platform | Desktop first (Win/macOS/Linux); iPad later → keep shaders Mobile-renderer compatible |
| Engine | Godot 4, C# for simulation, GDScript for UI/glue |
| Map | Real Oxford, OH geography, evolving 1809 → present → future |
| Tone | Realistic sim first, nostalgic/fun layer on top |
| Alcohol | Green Beer Day included; the game **never promotes or rewards underage drinking** (§23.5a hard rules) |
| Myaamia | Partnership track (§20); factual, university-published language only until the Myaamia Center reviews |
| Academics | Teaching-first, research real & important (teacher-scholar, Teaching Identity sweet spot 55–80) |
| Modes | Campaign + Sandbox + Challenges; Endless mode deferred |
| Utilities | Simple coverage radius |
| Simulation | Every student & faculty member fully simulated; only a subset rendered |
| Audience | Friends / Miami community + portfolio (non-commercial) |
| Placement | Free placement; real sites are optional ghost suggestions. Pure building placement, no zoning brushes |
| Real names | Real competitor school names (no logos); real Uptown business names (always positive, removable, in `/data/uptown.json`) |
| Regional campuses | Panels only in v1 |
| Terrain | Custom chunked mesh generator (decided after Spike B; v0.4) |

## Architecture rules (§30)
- **Simulation core in C#; UI and scene glue in GDScript.**
- **Agents are NOT Godot nodes.** Structure-of-arrays in plain C# (`float[] needSleep`, `int[] currentBuilding`, …),
  simulated on worker threads, decoupled from rendering. The main thread reads a snapshot; only a subset
  (~1,500–3,000) is drawn via `MultiMeshInstance3D`.
- **1 sim tick = 1 in-game hour.** Sub-systems run at different rates (§30.2): location/schedule and needs every
  tick; academics/health daily; enrollment per semester; admissions/budget/rankings yearly.
- **Seeded, deterministic RNG per system.** No `System.Random` without an explicit seed; no wall-clock or
  thread-order dependence in sim results.
- **No hard-coded tunables.** Every `[TWEAK]` value lives in `/data/*.json` (`balance.json` etc., §30.4).
- **Sim code is Godot-independent**: a plain C# class library (no `using Godot;`) so it can be unit-tested and
  benchmarked headless with `dotnet test` / `dotnet run`. Godot-facing C# lives in a thin bridge layer.
- Pathfinding: flow fields per destination building, cached, recomputed only when the map changes.
- Performance budget: full tick for 30,000 students + 2,500 faculty ≤ **8 ms** on a 4-core min-spec CPU at 1×.

## Environment (checked 2026-09-30)
| Tool | Status |
|---|---|
| Godot | 4.7.2-stable **mono** (.NET) at `C:\Tools\Godot\`, `GODOT` env var → console exe. GodotSharp targets `net8.0`; we target `net10.0` (works). |
| .NET | SDK 10.0.401, runtime 10.0.12 |
| git / git-lfs | 2.54.0 / 3.7.1 (LFS enabled per-repo with `--local`) |
| Python | 3.13.15 (`...\Programs\Python\Python313\python.exe`) |
| Blender | 5.2 at `C:\Program Files\Blender Foundation\Blender 5.2` |
| Dev machine | i9-14900HX (24C/32T), RTX 4070 Laptop, 32 GB — far above min spec; benchmarks must constrain to 4 threads and results must be caveated |

## Repo layout (§30.6, planned — created in Step 1)
```
addons/          editor plugins (map importer, data validators)
assets/models/   .glb from Blender (Git LFS)
assets/audio/
data/            JSON data + schemas (§30.4)
scenes/          world, UI, menus
scripts/sim/     C# simulation core (Godot-independent class library)
scripts/ui/      GDScript
tools/           offline Python pipeline + data validator
tests/           C# unit tests + headless performance benchmarks
docs/            ART_PIPELINE.md, outreach drafts, PHASE0_REPORT.md
```

## Build / run / test
Godot console exe: `$GODOT` env var (= `C:\Tools\Godot\Godot_v4.7.2-stable_mono_win64_console.exe`). Shells
opened before it was set may not see it; use the full path then. Python: `C:\Users\aidan\AppData\Local\Programs\Python\Python313\python.exe`.
```
dotnet build LoveAndHonor.sln                               # everything
dotnet test LoveAndHonor.sln                                # unit tests incl. data validation
dotnet run --project tools/DataValidator                    # validate /data (schemas + cross-refs) + assets/models
"$GODOT" --headless --path . --build-solutions --quit       # Godot import + C# build (bash; PowerShell: & $env:GODOT ...)
"$GODOT" --headless --path . -- --smoke-test                # boot scene prints "SMOKE ..." and quits
"$GODOT" --path . -e                                        # open editor
tools/.venv/Scripts/python tools/map_pipeline/build_map.py                    # regenerate data/map/ (downloads cached)
tools/.venv/Scripts/python -m pytest tools/map_pipeline                       # pipeline tests
dotnet run -c Release --project tests/LoveAndHonor.Sim.Benchmarks            # population benchmark, real map (exit 1 = over budget)
dotnet run -c Release --project tests/LoveAndHonor.Sim.Benchmarks -- --quick # gated run only
#   options: --map synthetic (Spike A campus), --edit-every N, --pace-all [--pace-speed S], --traffic-png FILE, --desire-png FILE
```
Always benchmark with `-c Release`. The gated run pins to 4 E-cores on hybrid Intel CPUs (conservative
stand-in for a 4-core min-spec); P-core / 1-thread / all-core runs are reference only.

## Project structure & conventions
- `LoveAndHonor.sln`: `LoveAndHonor.csproj` (Godot.NET.Sdk 4.7.2, net10.0) → ProjectReference →
  `scripts/sim/LoveAndHonor.Sim.csproj` (plain net10.0). Also `tools/DataValidator` and `tests/LoveAndHonor.Sim.Tests` (xUnit).
- The Godot csproj excludes `scripts/sim/**`, `tests/**`, `tools/**` from its compile glob. Those folders (and `docs/`)
  have `.gdignore`.
- Namespaces: `LoveAndHonor.Sim.*` (sim core), `LoveAndHonor.Bridge` (Godot nodes wrapping the sim),
  `LoveAndHonor.Tools`, `LoveAndHonor.Sim.Tests`.
- GDScript calls C# bridge methods by their PascalCase names (e.g. `_bridge.GetSimDescription()`).
- **The sim never does file I/O on `res://`.** Exported games pack `res://` into a .pck that `System.IO` can't read.
  The bridge reads data via Godot `FileAccess` and hands JSON text/streams to the sim.
- `LoveAndHonor.Sim.csproj` sets `Optimize=true` for Godot's `ExportRelease` config (plain SDK projects wouldn't).
  Editor runs use Debug, so in-editor sim timings are pessimistic; benchmark with `-c Release`.
- RNG: `DeterministicRng` (xoshiro256**, pinned by golden-value test) via `RngStreams.For("system", index)`.
  Never use `System.Random` or `string.GetHashCode()` for anything that affects sim state. Inside the parallel
  tick use `StatelessRandom` (hash of seed, agent, day, hour) so results don't depend on thread scheduling.
- Sim library layout (`scripts/sim/`): `Core/` (RNG, SimTime), `Data/` (typed configs, `IDataSource`, `SimData.Load`),
  `World/` (TileGrid, Campus, SyntheticCampusGenerator, RealCampusBuilder), `Pathing/` (FlowFieldSet: per-building fields,
  distance matrix, route cache + bounds), `Population/` (SoA `PopulationStore`, generator), `Engine/` (Simulation
  tick, ScheduleModel, NeedsModel, StateHash, SpikeWorld factory, SimRunner = sim thread + triple-buffered
  SimSnapshot), `View/` (VisualCrowd: engine-agnostic rendered-subset logic, unit-tested).
- Threading: `SimRunner` owns the sim thread; the main thread only calls `AdvanceRealTime`, `AcquireLatest`,
  `RequestTraffic`, `SpeedIndex`. Never read `PopulationStore` arrays from the main thread — use the snapshot.
- Godot bridge (`scripts/bridge/`): `GodotDataSource` (res:// via FileAccess), `PopulationSpikeHost` (world,
  runner, ground/buildings/walkers MultiMeshes, stats for GDScript). GDScript: `camera_rig.gd`, `debug_overlay.gd`.
- Presentation tunables live in `data/rendering.json`; colours there reference `branding.json` palette entries.
- Map (`data/map/`, generated — never hand-edit): projection is a transverse Mercator centred on campus (grid north
  = true north at the centre). Tile coords: x east, y SOUTH, origin NW corner (Godot +X east, +Z south). Heightmap
  `oxford_heightmap.r16` = 801² uint16 LE on tile corners every 5 m; rasters `*.u8` = 400² bytes, codes in
  `oxford_map.json`. Map size/tile size come from `balance.json`; the validator fails if they drift apart or a binary
  file has the wrong size (e.g. missing Git LFS). Pipeline settings: `data/map/map_config.json`.
- Real map in the sim: `RealMapLoader.Load(IDataSource)` → `RealMap` (TileGrid with LandState/Ownership/Protected/
  PathType/Types + Heightmap). Present-day land states come from rules in `data/map/land_states.json` (first match
  wins). Render space: world X east, Z south, Y = elevation − lowest point (231.6 m).
- Terrain: `View/TerrainMesher` (Godot-free) builds flat-shaded chunks (1 quad/tile, LOD steps, skirts); the bridge
  (`TerrainSpikeHost`) turns them into ArrayMeshes with visibility-range LODs and `assets/shaders/terrain.gdshader`
  (vertex colours are LINEAR, grid + hover in world space). Mouse picking = `Heightmap.Raycast`.
- Performance measurement on this laptop: FPS is capped (~120) by the hybrid-GPU display path; compare GPU/CPU
  render ms (`RenderingServer.ViewportGetMeasuredRenderTime*`) instead. Godot runs on the RTX 4070.
- Timeline (3c): `TimelineData`/`FeatureBuilding`/`HistoricBuilding` (World/Timeline.cs) join timeline.json with OSM
  outlines; undated buildings show only in the present year unless the UI toggle is on. `LandHistory` derives land state
  + road visibility per tile per year from `data/map/land_history.json` (PLACEHOLDER rules; square "Mile Square" town
  growth from Uptown, clearing outward, campus growth from dated buildings); invariant tested: present year == today's
  map. `TileColorizer` paints a tiles² RGBA texture (seasons blended by day; road surface by era from eras.json);
  terrain shader samples it (vertex colours are white). Buildings: `BuildingMeshBuilder` (bridge) extrudes footprints,
  merged per chunk; `buildings.gdshader` collapses vertices outside UV2=(built, demolished). Sun: `SolarPosition`
  (NOAA), standard time only.
- Art pipeline (`docs/ART_PIPELINE.md`; numbers in `data/art_pipeline.json`, typed as `ArtPipelineConfig`): Blender
  sources in `art/blend/` (.gdignore), exported `.glb` only in `assets/models/<category folder>/` via
  `tools/blender/lh_export.py export_glb` (never hand-export). Names `<kit|bldg|hero|char|prop|tree>_<name>[_<variant>]`,
  LOD objects `_LOD0.._LOD3`, materials `pal_<branding colour>[_n]` or `mat_*`. `project.godot [importer_defaults]`
  makes `scripts/import/lh_post_import.gd` the post-import script (sets visibility-range LODs + `lh_category`/`lh_lod`
  meta). `PaletteMaterials` (bridge) swaps materials at runtime by name so colours follow branding.json.
  `tools/DataValidator` also runs `ModelValidation` (GLB JSON chunk: names, folder, budgets, LOD ratios, materials,
  scale, pivot) — DataValidator now references the sim library. Blender exe: `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`.
- Real-map sim (Phase 1 1a): `SimWorld.CreateReal(data, source, year)` → `RealCampusBuilder` (timeline entries standing
  that year, kinds mapped by `real_campus.json`, entrance = nearest path tile in the main walkable component; town
  houses grouped into weighted housing zones). `SimWorld.CreateSynthetic` keeps the Spike A campus.
- Flow fields (`FlowFieldSet`): ushort costs (`CostScale` 30/tile), full build = Dial's bucket queue, directions picked
  by a fixed rule from the (unique) costs → incremental updates are bit-identical to a full rebuild (tested on 20 random
  maps). Housing zones have no field (routes = reversed). Map edits: `Simulation.ApplyTileEdits` (sim thread, between
  ticks) → background update into a spare buffer → swapped in exactly `pathing_rebuild_latency_ticks` later (the sim
  waits if not ready) → deterministic. Never read `FlowFieldSet.CostsTo/DirectionsTo` off the sim thread.
- Foot-traffic route tracing runs as a thread-pool work item after each tick (overlapping idle time); at the 23:00
  tick it also runs `TileGrid.UpdateWear` (today = FootTraffic - TrafficAtMidnight). Call `Simulation.SyncFootTraffic()`
  before reading `FootTraffic`/`Wear` (`StateHash.Compute(sim, grid)` and SimRunner snapshots do it).
- Walking model (1b): `TileType.Rough` = unpaved non-lawn (cost `rough_cost_multiplier`); lawn = `real_campus.json
  lawn_land_states` (university only). Edge costs add half of `path_exit_penalty_m` on every path<->unpaved step (both
  directions, so routes stay symmetric). Class/Teach walks leave before the hour (`Simulation.ProcessChunk`); free time
  is rolled per `schedules.json student.free_block_hours` block, staggered by agent index.
- Benchmark methodology: the gated run is back-to-back ticks with a path edit every 48 ticks (paced at 1x while a
  rebuild is pending); a reference run paces every tick at 1x, because idle cores start ticks slower (~+1 ms here).
- Downloads: never put the user's email or other personal data in request headers/URLs (the pipeline's
  User-Agent is generic; optional `LH_PIPELINE_CONTACT` env var).
- Tick determinism: work is split into fixed-size chunks (`performance.agent_chunk_size`), each agent touches
  only its own data, chunk results reduce in chunk order → 1 thread and N threads give identical `StateHash`
  (tested). Keep it that way: no shared mutable state inside `ProcessChunk`.
- The tick must not allocate per agent (benchmark reports GC; currently 0 collections during measurement).
- Data: every `data/**/*.json` declares `"$schema"`; `_`-prefixed keys are comments; unverified real-world facts
  carry `"verified": false`. New data file → add a schema + (if needed) cross-checks in `tools/DataValidator/DataValidation.cs`.
- Branding: active profile `data/branding.json`, fictional stand-in `data/branding/standin.json`. Same schema.
- Line endings: LF in the repo (`.gitattributes`); binary art/audio/geodata via Git LFS.

## Content sensitivity (applies to any text or data written here)
- Alcohol: only 21+ agents drink; underage drinking only ever has negative outcomes; no drinking rewards/achievements.
- Myaamia: no invented language or imagery; no stereotyped content ever.
- Real people: no current administrators/faculty/coaches/students as characters.
- Uptown businesses: never attach negative events to a named real business.
- Mental health / hazing: serious, consequential, never punchlines; 988 Lifeline info for mental-health events.
