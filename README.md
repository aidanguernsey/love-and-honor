# Love & Honor

A low-poly 3D university city-builder set in Oxford, Ohio — run Miami University from its 1809 charter to
the present day and beyond. Built with Godot 4 (.NET) and C#.

> **Unofficial fan project — not affiliated with or endorsed by Miami University.**
> Free and non-commercial. Real names and marks are used pending permission (see `GAME_DESIGN.md` §36);
> all branding lives in `data/branding.json` and can be swapped for the fictional stand-in in `data/branding/`.

Design: [`GAME_DESIGN.md`](GAME_DESIGN.md). Status: **Phase 0** (foundations and technical spikes).

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| Godot | **4.7.2 .NET** (the "mono" build) | Set the `GODOT` environment variable to the `_console.exe` for command-line use |
| .NET SDK | **10.0** | Godot's C# API targets .NET 8; this project targets `net10.0` (.NET 8 support ends Nov 2026) |
| Git + Git LFS | any recent | Run `git lfs install` once per machine |
| Python | 3.13 | Offline map pipeline in `tools/` (Spike B) |
| Blender | 5.x (optional) | Art pipeline (`docs/ART_PIPELINE.md`, Step 4) |

## Build, run, test

All commands run from the repo root.

```bash
dotnet build LoveAndHonor.sln
```

```bash
dotnet test LoveAndHonor.sln
```

Validate every file in `data/` against its schema, plus cross-file references, then check every model in
`assets/models/` (names, triangle budgets, LODs, materials — see `docs/ART_PIPELINE.md`):

```bash
dotnet run --project tools/DataValidator
```

Population benchmark (headless, no renderer; exits with code 1 if the tick budget is missed). Runs on the real
Oxford map and lays/removes a path every 48 ticks to exercise background pathfinding updates. Options after `--`:
`--quick` (gated run only), `--map synthetic` (Spike A campus), `--edit-every N`, `--pace-all` (pace every tick like the
game at 1x), `--traffic-png FILE` (foot-traffic heatmap), `--desire-png FILE` (desire paths worn into lawns):

```bash
dotnet run -c Release --project tests/LoveAndHonor.Sim.Benchmarks
```

Godot commands below are for **PowerShell** (the Windows default). They use the `GODOT` environment
variable; open a new terminal after setting it. In bash (Git Bash, macOS, Linux), replace `& $env:GODOT`
with `"$GODOT"`.

Open the project in the Godot editor (it builds the C# on first open):

```powershell
& $env:GODOT --path . -e
```

Run the game directly:

```powershell
& $env:GODOT --path .
```

Spike A scene (32,500 simulated agents, ~2,000 drawn): click **Spike A** on the boot screen, or run it directly:

```powershell
& $env:GODOT --path . res://scenes/spikes/population_spike.tscn
```

Controls: WASD / middle-drag pan · wheel zoom · Q/E / right-drag orbit · R/F tilt · Space pause ·
1–4 speed (1×/2×/4×/8×) · O foot-traffic heatmap · Esc back to menu. Launch options (after `--`):
`--speed=N` (0 = pause … 4 = 8×), `--camera=x,z,distance,pitch,yaw`, `--spike-smoke` (print stats after 6 s and quit).
Tick times shown in the editor come from a Debug build of the sim; use the benchmark for real numbers.

The game: click **Play** on the boot screen for Campaign Chapter 1, "The Hill" (starts 1 November 1824 on the map as it
stood then), or **Preview — 2026 campus** for the modern campus with everyone simulated. Or run it directly:

```powershell
& $env:GODOT --path . res://scenes/game/game.tscn
```

Controls: WASD / middle-drag pan · wheel zoom · Q/E / right-drag orbit · R/F tilt · Space pause/resume · 1–4 speed
(1×/2×/4×/8×) · O overlays (ownership, foot traffic) · C clear forest · L buy land (drag a rectangle; the cost shows
before you let go) · B build menu (or the category buttons; pick a building, then click the map: the ghost is green
when it can be built, red with the reason when not) · Z/X turn the building 15° · Del cancel construction · H Heritage
Project sites on/off · N day/night cycle on/off (both remembered) · G build grid · Esc stop the tool / main menu. Hover a
building for its name or construction progress. Desire paths wear into the campus lawns over a few in-game weeks.
Launch options (after `--`): `--scenario=chapter1_the_hill|preview_2026`, `--speed=N`, `--camera=x,z,distance,pitch,yaw`,
`--day-night=on|off`, `--cash=N` (starting cash), `--demo-land` (a sample clearing order and purchase), `--demo-build`
(two buildings near Old Main, then Elliott Hall on its real site once it's offered in 1825; add `--cash=20000`),
`--build-item=ID`, `--build-rotation=DEG`, `--ghost-at=x,z` (screenshots of the ghost), `--game-smoke[=seconds]`
(print stats and quit), `--screenshot=FILE` (with `--game-smoke`).

Spike B terrain (real Oxford, 1 m lidar + OSM): click **Spike B** on the boot screen, or run it directly:

```powershell
& $env:GODOT --path . res://scenes/spikes/terrain_spike.tscn
```

Point at the ground to inspect a tile or building. The bottom bar is the 1809 → 2026 timeline (Play, speed, Space
play/pause, [ / ] step a year), plus season and time-of-day sliders. G toggles the build grid. Launch options:
`--year=N`, `--day=N`, `--hour=H`, `--play`, `--camera=x,z,distance,pitch,yaw`, `--spike-smoke`, `--no-vsync`.
Land use before today is a placeholder model (`data/map/land_history.json`); building dates come from
`data/timeline.json` (unverified, see `docs/research/BUILDING_DATES.md`). Terrain approach: `docs/TERRAIN_COMPARISON.md`.

Step 4 art import check (a placeholder kit piece tiled into a facade, its LODs side by side): click
**Step 4 — Art import check** on the boot screen, or run it directly:

```powershell
& $env:GODOT --path . res://scenes/spikes/art_import_test.tscn
```

Regenerate the placeholder kit piece with Blender (writes the `.glb` and its `.blend` source):

```powershell
& "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --background --factory-startup --python tools\blender\make_example_kit.py
```

The whole art workflow (naming, budgets, LODs, materials, checks) is in [`docs/ART_PIPELINE.md`](docs/ART_PIPELINE.md).

Headless smoke test (build C#, boot the main scene, print what it loaded, quit):

```powershell
& $env:GODOT --headless --path . --build-solutions --quit
```

```powershell
& $env:GODOT --headless --path . -- --smoke-test
```

## Real map data (Spike B)

The real Oxford terrain and OpenStreetMap layers in `data/map/` are generated by a Python pipeline; see
[`tools/map_pipeline/README.md`](tools/map_pipeline/README.md) for setup, running it, and manual download steps.

```powershell
tools\.venv\Scripts\python tools\map_pipeline\build_map.py
```

## Project layout

```
addons/            Godot editor plugins (map importer, data validators)
art/blend/         Blender sources (.blend, Git LFS) — Godot ignores this folder
assets/            models (.glb), audio, textures — binary files go through Git LFS
data/              all content & balance data as JSON, each with a "$schema" in data/schemas/
scenes/            Godot scenes
scripts/sim/       C# simulation core — plain .NET class library, no Godot dependency
scripts/bridge/    thin C# Godot nodes that expose the sim to scenes/GDScript
scripts/ui/        GDScript UI and scene glue
scripts/import/    Godot post-import script for models (LOD setup)
tools/             offline tools: DataValidator (C#), map pipeline (Python), Blender scripts
tests/             C# unit tests (+ headless performance benchmarks from Spike A)
docs/              pipeline docs, outreach drafts, phase reports
```

`scripts/sim`, `tests`, `tools` and `docs` contain a `.gdignore` so the Godot editor doesn't scan them.
The Godot project (`LoveAndHonor.csproj`) references the sim library as a normal project reference.

## Exporting

`export_presets.cfg` has a **Windows Desktop** preset (output `build/windows/`, git-ignored). It includes
`*.json, *.r16, *.u8` as non-resource files: without them the game can't find `data/` (the map's heightmap and tile
rasters are raw binary files). Needs the Godot 4.7.2 .NET export templates (Editor → Manage Export Templates).

```powershell
& $env:GODOT --headless --path . --export-release "Windows Desktop" build/windows/LoveAndHonor.exe
```

Ship the whole `build/windows/` folder (the `.exe`, the `.pck` and the `data_LoveAndHonor_windows_x86_64` folder with the
.NET runtime). Release builds can't open a scene from the command line; `LoveAndHonor.exe -- --play` starts the game
directly.

## Data conventions

- Every JSON file in `data/` starts with `"$schema": "<relative path to data/schemas/...>"`. Editors like
  VS Code use it for autocomplete; the validator uses it to check the file.
- Keys starting with `_` (e.g. `_comment`) are free-form notes and are ignored.
- Every tunable number lives in data, never in code. Real-world facts carry `"verified": false` until checked
  against a source.
- Building definitions: one file per building in `data/buildings/`, file name = `id`.

## Credits & data licenses

- Map data © OpenStreetMap contributors, ODbL (attribution will be shown in-game once OSM data is used).
- Elevation: USGS 3D Elevation Program (public domain); 1 m lidar, OH Statewide Phase 3 (2020–2023).
