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

Validate every file in `data/` against its schema, plus cross-file references:

```bash
dotnet run --project tools/DataValidator
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

Headless smoke test (build C#, boot the main scene, print what it loaded, quit):

```powershell
& $env:GODOT --headless --path . --build-solutions --quit
```

```powershell
& $env:GODOT --headless --path . -- --smoke-test
```

## Project layout

```
addons/            Godot editor plugins (map importer, data validators)
assets/            models (.glb), audio, textures — binary files go through Git LFS
data/              all content & balance data as JSON, each with a "$schema" in data/schemas/
scenes/            Godot scenes
scripts/sim/       C# simulation core — plain .NET class library, no Godot dependency
scripts/bridge/    thin C# Godot nodes that expose the sim to scenes/GDScript
scripts/ui/        GDScript UI and scene glue
tools/             offline tools: DataValidator (C#), map pipeline (Python)
tests/             C# unit tests (+ headless performance benchmarks from Spike A)
docs/              pipeline docs, outreach drafts, phase reports
```

`scripts/sim`, `tests`, `tools` and `docs` contain a `.gdignore` so the Godot editor doesn't scan them.
The Godot project (`LoveAndHonor.csproj`) references the sim library as a normal project reference.

## Data conventions

- Every JSON file in `data/` starts with `"$schema": "<relative path to data/schemas/...>"`. Editors like
  VS Code use it for autocomplete; the validator uses it to check the file.
- Keys starting with `_` (e.g. `_comment`) are free-form notes and are ignored.
- Every tunable number lives in data, never in code. Real-world facts carry `"verified": false` until checked
  against a source.
- Building definitions: one file per building in `data/buildings/`, file name = `id`.

## Credits & data licenses

- Map data © OpenStreetMap contributors, ODbL (attribution will be shown in-game once OSM data is used).
- Elevation: USGS 3D Elevation Program (public domain).
