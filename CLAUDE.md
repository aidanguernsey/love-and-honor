# CLAUDE.md — Love & Honor

A low-poly 3D university city-builder set at Miami University (Oxford, Ohio), 1809 → present → future.
Godot 4 (.NET build) + C#. Non-commercial fan / portfolio project.
The full design is in `GAME_DESIGN.md` (v0.3). **Read the relevant section before working on a system.**

## Current phase
**Phase 0 — Foundations & technical spikes** (§37). Build no gameplay content beyond what the spikes need.
Steps: (1) scaffold → (2) Spike A population at scale → (3) Spike B Oxford terrain + timeline →
(4) art pipeline prep → (5) outreach drafts → (6) Phase 0 report.
Status: Step 1 done. Step 2a (headless population sim + benchmark) done — PASS, p95 ≈ 3.5 ms on 4 E-cores
(results: `docs/benchmarks/spike-a-2026-09-30.txt`). Next: **2b** (rendering scene, camera, debug overlay).

### Findings to carry into the Phase 0 report
- Flow-field rebuild: ~1.4 s on 4 E-cores / ~0.3 s on 4 P-cores for 41 buildings (Dijkstra per building). The
  real game has more buildings → must rebuild asynchronously (double-buffer fields) and/or use a faster algorithm.
- Tick scaling: 4 P-cores ≈ 3.1× one P-core; 4 E-cores only ≈ 1.25× one E-core (shared-cluster limits).
- Occasional single-tick max spikes (6-11 ms) from OS preemption; p99 stays ≈ 4 ms.
- Lateness: agents depart on the hour, so any walk > class-change window (10 min) counts as late (avg walk
  11.9 min on the spike map). Phase 1 needs "leave before the hour" departures.
- Free-hour activity is re-rolled every hour → ~14.8k walks/tick (stress case); smooth it in Phase 1.

### Decisions made during Phase 0 (by the user)
- Spike A pass criterion: **p95 tick time ≤ 8 ms** over 1,000 ticks (avg and max reported too). (2026-09-30)
- Spike A split into 2a / 2b checkpoints. (2026-09-30)
- Proposed, not objected to: sim resolves walks within the hourly tick (depart/arrive minutes); rendered
  walkers use a cosmetic walk speed decoupled from game time. Spike campus adds off-campus housing blocks
  and doesn't enforce capacity. (Record as a suggested GAME_DESIGN.md update in the Phase 0 report.)

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
dotnet run --project tools/DataValidator                    # validate /data (schemas + cross-refs)
"$GODOT" --headless --path . --build-solutions --quit       # Godot import + C# build (bash; PowerShell: & $env:GODOT ...)
"$GODOT" --headless --path . -- --smoke-test                # boot scene prints "SMOKE ..." and quits
"$GODOT" --path . -e                                        # open editor
dotnet run -c Release --project tests/LoveAndHonor.Sim.Benchmarks            # Spike A benchmark (exit 1 = over budget)
dotnet run -c Release --project tests/LoveAndHonor.Sim.Benchmarks -- --quick # gated run only
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
  `World/` (TileGrid, Campus, SyntheticCampusGenerator), `Pathing/` (FlowFieldSet: per-building Dijkstra fields,
  distance matrix, route cache), `Population/` (SoA `PopulationStore`, generator), `Engine/` (Simulation tick,
  ScheduleModel, NeedsModel, StateHash, SpikeWorld factory).
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
