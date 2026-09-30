# CLAUDE.md — Love & Honor

A low-poly 3D university city-builder set at Miami University (Oxford, Ohio), 1809 → present → future.
Godot 4 (.NET build) + C#. Non-commercial fan / portfolio project.
The full design is in `GAME_DESIGN.md` (v0.3). **Read the relevant section before working on a system.**

## Current phase
**Phase 0 — Foundations & technical spikes** (§37). Build no gameplay content beyond what the spikes need.
Steps: (1) scaffold → (2) Spike A population at scale → (3) Spike B Oxford terrain + timeline →
(4) art pipeline prep → (5) outreach drafts → (6) Phase 0 report.
Status: environment checked, CLAUDE.md written. Step 1 not started.

## Working rules
- Work in small steps. At the end of each step: stop, summarize, explain how to verify, and wait for the user's go-ahead.
- Commit after each working step with a clear message.
- **Never edit `GAME_DESIGN.md`.** Propose design-doc changes as a list for the user to approve.
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
| Open question | Terrain: custom mesh generator vs. plugin (Terrain3D) — decide after Spike B |

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
| Godot | 4.7.2-stable **mono** (.NET) at `C:\Users\aidan\Downloads\Godot_v4.7.2-stable_mono_win64\...` — not on PATH. GodotSharp targets `net8.0`. |
| .NET | Runtime 10.0.11 only — **no SDK installed** (required) |
| git / git-lfs | 2.54.0 / 3.7.1 ✔ |
| Python | **Not installed** (only the Microsoft Store alias) — needed for `/tools` pipeline |
| Blender | Not installed (optional, Step 4) |
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
_To be filled in during Step 1 once the .NET SDK is installed and the projects exist._

## Content sensitivity (applies to any text or data written here)
- Alcohol: only 21+ agents drink; underage drinking only ever has negative outcomes; no drinking rewards/achievements.
- Myaamia: no invented language or imagery; no stereotyped content ever.
- Real people: no current administrators/faculty/coaches/students as characters.
- Uptown businesses: never attach negative events to a named real business.
- Mental health / hazing: serious, consequential, never punchlines; 988 Lifeline info for mental-health events.
