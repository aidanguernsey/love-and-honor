# Love & Honor — Phase 0 report

*2026-09-30 · Phase 0 "Foundations & technical spikes" (GAME_DESIGN.md §37) · for approval*

## 1. Summary

The technical foundations hold up. Both spikes pass, and nothing found in Phase 0 forces a change to a Locked
Decision.

- **Spike A: PASS.** 32,500 fully simulated agents (30,000 students + 2,500 faculty) run a tick in **p95 3.5 ms**
  against the **8 ms** budget. This was measured on 4 slow efficiency cores as a stand-in for a 4-core minimum-spec
  CPU. No garbage collections happened during the run, results are identical on 1 thread and on N threads, and a
  sample of 2,000 walkers is drawn at the display's frame cap.
- **Spike B: done.** The real Oxford terrain comes from 1 m USGS lidar, with OpenStreetMap layers on a 400 × 400
  tile grid, a build grid and a tile inspector. A **1809 → 2026 timeline** repaints land use and roads and
  shows or hides 177 researched Miami buildings, with seasons and a correct sun.
  - **Terrain decision: custom mesh generator** (your call; it closes open question §38.1).
- **Art (Step 4): prep only**, as you chose.
  - Built: the Blender → glTF → Godot pipeline, naming rules and budgets, automatic model checks, and one
    placeholder kit piece taken through the whole pipeline.
  - Decided afterwards: window panes drawn by a shader, a procedural kit assembler for buildings, era looks via
    shaders, and vertex-animation textures for crowds.
- **Outreach (Step 5): drafted, not sent.** Trademark and licensing, the Myaamia Center (with a one-page summary),
  and an Uptown courtesy note are in `docs/outreach/`. §37 lists *sending* the licensing and Myaamia emails as a
  Phase 0 item, so that part is now yours.
- **Biggest risk going into Phase 1:** pathfinding rebuild time. Rebuilding the flow fields takes about 1.4 s for
  41 buildings on the slow cores; the real campus has 150+. It must move to a background thread (§4).
- **Scope warning:** as written, Phase 1 is more than 4–6 weeks of work (§5). I propose cuts.

What I need from you is in §7: approve or reject the design-doc updates, confirm the carried-over defaults, and
answer the Phase 1 questions.

### Phase 0 checklist (§37)

| §37 item | Status | Where |
|---|---|---|
| Godot 4 .NET project, repo, Git LFS, folders, data schema | Done | `CLAUDE.md`, `README.md`, `data/schemas/` (15 schemas + validator) |
| Spike A — population, flow fields, MultiMesh, §30.2 budget | **Pass** | `docs/benchmarks/spike-a-2026-09-30.txt`, `scenes/spikes/population_spike.tscn` |
| Spike B — heightmap, build grid, land states, timeline 1809 → 2026 | Done | `scenes/spikes/terrain_spike.tscn`, `docs/TERRAIN_COMPARISON.md`, `docs/research/BUILDING_DATES.md` |
| Art spike — Georgian kit + 3–5 buildings | **Prep only** (your decision); the kit and buildings move to Phase 1 | `docs/ART_PIPELINE.md`, `scenes/spikes/art_import_test.tscn` |
| Send licensing & Myaamia emails | **Drafted**; sending is yours | `docs/outreach/` |

Tests: 74 unit tests (sim, data, map, timeline, model checks) pass. `dotnet run --project tools/DataValidator`
checks all data and models.

## 2. Benchmark results

### Spike A — simulation tick (headless, `-c Release`, 1,000 measured ticks after a 1-week warm-up)

Budget (§30.2, your pass criterion): **p95 tick time ≤ 8 ms** at 30,000 students + 2,500 faculty.

| Run | avg | p50 | **p95** | p99 | max | Result |
|---|---|---|---|---|---|---|
| **4 E-cores (gated min-spec stand-in)** | 2.19 | 2.07 | **3.47** | 3.98 | 6.39 | **PASS** |
| 4 P-cores | 0.67 | 0.62 | 1.05 | 1.38 | 1.60 | pass |
| 1 P-core, 1 thread (reference) | 1.91 | 1.87 | 2.63 | 2.95 | 5.08 | pass |
| All 32 threads (reference) | 2.16 | 2.05 | 3.63 | 3.94 | 6.46 | pass |

All times are in ms. Repeated gated runs gave p95 3.5–4.0 ms, so there's about 2× headroom.

- **Workload:** 14,836 walks per tick on average (peak 30,851). That is a stress case: free-time activity is
  re-rolled every hour, and Phase 1 will smooth it.
- **Garbage collection:** none during measurement. Population arrays take about 3.8 MB.
- **Determinism:** the same seed gives the same state hash on 1 thread and on N threads (tested).
- **Caveats:**
  - The development laptop (i9-14900HX) is far above min spec. The 4 E-cores are a conservative stand-in, not a
    real 4-core machine.
  - Scaling is poor on E-cores: 4 threads are only about 1.25× faster than 1 (on P-cores, about 3.1×). So keep
    the single-thread cost low.
  - Occasional single slow ticks (6–11 ms max, p99 about 4 ms) come from the operating system interrupting the
    process. They don't matter, because the sim runs off the main thread.
- **The sim ran on a synthetic 41-building campus, not the real map.** Moving it to the real map is the first
  Phase 1 task, followed by a re-benchmark.

### Spike A — rendering (`population_spike.tscn`)

2,000 walkers are drawn as a sample within a detail radius of the camera. The scene runs at about 118 FPS, which is
this laptop's display cap, with 0 dropped sim ticks at 8× speed. The sim runs on its own thread and hands the
renderer a finished snapshot of each tick; the main thread never waits for the sim.

### Spike B — terrain and timeline (RTX 4070 laptop GPU; FPS is display-capped, so GPU/CPU ms are the real measure)

| Measure | Result |
|---|---|
| Terrain, custom generator (4 views) | GPU 0.5–1.5 ms/frame, 37k–305k triangles, 100 chunks × 3 LODs |
| Terrain3D, same views | GPU 1.3–1.9 ms/frame, 253k–677k triangles |
| Timeline: repaint on a year change | 5–11 ms on the main thread |
| Timeline: buildings | 3,143 footprints ≈ 66k triangles; GPU 1.1–1.6 ms |
| Map data | 4 km × 4 km; 1 m lidar → 5 m heightmap (801²); relief 231.6–296.8 m (65 m) |

### Step 4 — art import check (`art_import_test.tscn`)

A 12-piece placeholder facade plus 3 LOD samples renders at GPU 0.56 ms. At 110 m the facade switches to LOD1,
and the triangles in the frame drop from 13.2k to 4.2k, so the LOD setup works.

## 3. Terrain recommendation

**Use the custom chunked mesh generator.** This was your decision, based on `docs/TERRAIN_COMPARISON.md`.

- It matches the locked low-poly style natively. Facets are fixed in world space and one quad equals one tile.
  Terrain3D's camera-centred geometry would make facets shift as the camera moves.
- The tile grid, hover highlight, per-tile colours and timeline repaint are trivial on it.
- It uses fewer triangles and less GPU time in 3 of the 4 views.
- It has no native-plugin dependency, and Terrain3D officially supports only Godot 4.4–4.6.
- It uses plain meshes, so it's compatible with the Mobile renderer for the later iPad version.
- **Costs:** more draw calls (up to about 180), LOD popping (no smooth blending), and no free editor tools. We
  don't need sculpting, because the terrain comes from real data.
- **Follow-ups:**
  - Rebuild only affected chunks when land changes.
  - Merge far chunks.
  - Draw paths and roads as their own meshes (see §4).
  - Chunk size needs your call (§7.2 #9).

## 4. Risks

| # | Risk | Likelihood / impact | Mitigation | When |
|---|---|---|---|---|
| R1 | **Flow-field rebuild takes 1.4 s for 41 buildings** (E-cores); the real campus has 150+ on 160k tiles, so placing a path would freeze the game | High / High | Rebuild on a background thread and swap the result in when ready; a faster algorithm (bucket queue); rebuild only the fields a change affects; re-benchmark on the real map | Phase 1, first |
| R2 | **Sim not yet on the real map** (the synthetic campus has 41 buildings) | Certain / High | Hook the sim to `RealMap` and the timeline; re-run the gated benchmark | Phase 1, first |
| R3 | **Phase 1 scope larger than 4–6 weeks** | High / Medium | Cuts proposed in §5 | Now |
| R4 | **Procedural kit assembler complexity.** Real OSM footprints are irregular, and hip roofs from a straight skeleton are notoriously fiddly | Medium / Medium | Phase 1 needs only rectangles and L-shapes (the player places catalogue buildings). Arbitrary real footprints wait for Phase 2 (the modern campus) | Phase 1 v1 → Phase 2 |
| R5 | **Art throughput.** The kit (~35 pieces), the window shader, and Elliott / a generic hall are all still to make | Medium / Medium | Scripted placeholder pieces first, final art later; per-piece budgets in `data/art_pipeline.json` | Phase 1 |
| R6 | **Export untested.** Presets must include `*.json, *.r16, *.u8`; we target .NET 10 while GodotSharp targets .NET 8 (fine in the editor, unproven in an export) | Medium / High if found late | Create presets and do a Windows test export early in Phase 1 | Phase 1 |
| R7 | **Branding permission pending** | Unknown / Low | Everything is swappable via `branding.json`, and a stand-in profile exists and is tested. The title is the one non-data item and is in the licensing email | When a reply arrives |
| R8 | **Myaamia review timing** | Unknown / Low for Phase 1 | Published facts only until reviewed; Chapter 6 is far off | When a reply arrives |
| R9 | **Historic facts unverified.** 177 building entries are sourced but `verified: false`; 13 conflict; land use before today is a placeholder model | Certain / Medium | University Archives visit (King Library); hand-trace Sanborn maps for the Phase 1 area (§30.5 step 3) | Phase 1–2 |
| R10 | **Min-spec performance is inferred, not measured** | Medium / Medium | Profile on a real 4-core laptop before Phase 2's full population on the real map | Phase 2 |
| R11 | **Paths are 2.5 m wide on 10 m tiles**, so they draw as whole tiles and the campus looks path-dense | Certain / Medium (looks) | Paths and roads become their own meshes; tiles keep the logic | Phase 1 |
| R12 | **macOS, Linux and the Mobile renderer are untested** | Low now | Test once exports exist; test Mobile before any iPad work (your decision) | Phase 3 |
| R13 | **No remote backup.** 15 commits and 27 Git LFS files exist only on this laptop (about 15 MB) | Low likelihood / High impact | Add a private remote with LFS and push | Now (your call) |

## 5. Phase 1 — "The Hill" (Campaign Chapter 1): proposed breakdown

**Goal (§37, §4.1):**
- Start from the 1809 map, with classes beginning in 1824 `[VERIFY]`: empty land, one building (the Old Main
  site) and a tiny budget.
- Clear and acquire land, place the first buildings, lay paths and let desire paths form.
- Run the calendar and the budget.
- Grow enrollment from dozens to about 250, with needs and happiness and a handful of faculty (teaching plus
  early scholarship).
- Add 10–15 era events and codex entries, save/load, and the time-lapse.
- **Win:** 250 students and a first residence hall (Elliott-style).

As before, the work is split into checkpoints, each ending with a stop for your review. Sizes are relative
(S ≈ a day or two, M ≈ several days, L ≈ a week or more).

| Step | Work | Size | Why now / notes |
|---|---|---|---|
| **1a** Real-map sim | Sim on `RealMap` (160k tiles). Flow-field rebuild on a background thread with double buffering; faster algorithm. Re-benchmark (gated 4 E-cores, ~150 real buildings) | L | Retires R1 and R2 before anything is built on top |
| **1b** Walking model v2 | Leave before the hour, so lateness = arriving after the 10-minute window. Paths strongly preferred (grass cost + step-off penalty). Wear from recent traffic with grass regrowth (your feedback). Smoother free-time activity | M | Tune with you against real shortcuts |
| **1c** Game shell | Main game scene (terrain + sim + camera). Calendar and speeds (§6); HUD skeleton (§27.1); date, season and sun from the sim clock. Windows export preset + test export | M | Retires R6 |
| **1d** Land | Start state at 1809. Clearing (cost, time, slower in winter), acquisition at era prices, ownership overlay (§5.1b). Town growth driven by the placeholder rules | M | Uses `LandHistory`; the rules stay placeholders until the maps are traced |
| **1e** Placement | Era-gated catalogue (1820s). Placement checks: land state, ownership, slope, path access. Rotation, construction time with summer/winter speed (§12.1). Heritage Project ghost sites from `timeline.json` (Old Main, Elliott, Stoddard) | L | |
| **1f** Buildings v1 | Kit assembler v1 (rectangles and L-shapes, hip roofs, bays, door/portico, cupola). Window-pane shader; era shader settings. About 12 core kit pieces as scripted placeholders. Elliott and a generic hall assembled from recipes | L | Carries the deferred §37 art spike. Arbitrary OSM footprints wait for Phase 2 |
| **1g** Paths | Player-built paths (dirt → gravel → brick by era) drawn as meshes. Desire paths from 1b. Pave a desire path (the Slant Walk hook) | M | Retires R11 |
| **1h** People | Tiny enrollment (admissions only by yearly intake for now), needs and happiness, a small 1820s class schedule, a handful of faculty (teaching + minimal scholarship, §13.7). Where students live (in halls or boarding in town `[VERIFY history]`) | L | Same sim, far smaller population |
| **1i** Economy | Budget: tuition, land rents `[VERIFY]`, state support `[VERIFY]`; construction and upkeep; yearly budget screen; Trustee Confidence (§3) | M | Historic finances need research |
| **1j** Chapter 1 | Start state, objectives, win and fail conditions. 10–15 sourced era events. Codex (History Book) entries with sources. Minimal advisor messages | M | Content research and writing |
| **1k** Save/load + time-lapse | Binary saves with a JSON header, versioned (§31); autosave each semester. Time-lapse replay from the recorded player actions and the timeline | M | GIF/MP4 export deferred |

**Proposed cuts to fit 4–6 weeks.** Even with them, this is roughly 6–9 weeks for one person with Claude; the
§37 estimate looks optimistic.
- Time-lapse *export* moves to Phase 3; replay stays.
- 10 events, not 15.
- Scholarship stays minimal (one research counter per faculty member).
- No weather beyond seasons.
- No town business simulation (Uptown is Phase 2).
- Kit pieces stay placeholder quality.

## 6. What Phase 0 changed or confirmed about the design (details in §7.1)

- **Movement:** the sim settles each walk within the hourly tick. Drawn walkers are a sample moving at a
  visual-only speed near the camera. At 1× an in-game hour lasts 83 ms, so real walking speed can't be shown.
- **Buildings:** a procedural kit assembler builds them from data recipes. Hero landmarks stay hand-modelled.
- **Art look:** window panes are drawn by a shader so halls fit the 1–5k triangle budget. Era looks are shader
  settings.
- **Desire paths stay but are less common:** people prefer paved paths and cut across grass only when it clearly
  saves time; only regularly used shortcuts wear to dirt (your first-hand observation).
- **Data added in Phase 0:** timeline, map pipeline, rendering and art-pipeline rules; every number is in `/data`.

## 7. For your decision

### 7.1 Suggested GAME_DESIGN.md updates — approve, reject or discuss each

I haven't edited the design doc. Once you approve, I'll apply the approved rows and bump the version.

| # | Section | Suggested change | Why |
|---|---|---|---|
| U1 | §30.3, §38.1 | Terrain: **custom chunked low-poly mesh generator**; move open question 1 to Resolved | Your decision after the Terrain3D trial |
| U2 | §30.3 | Chunks of **400 m (40 × 40 tiles) with 3 distance LODs** instead of "e.g. 64 × 64 m" | 64 m doesn't divide the 4 km map; smaller chunks mean more draw calls. *Marked Discuss (decision #9)* |
| U3 | §30.3 | Remove **"zoning"** from the tile data layer list | Conflicts with §12.2 (locked: no zoning brushes) |
| U4 | §30.2 | State the budget as **p95 ≤ 8 ms over 1,000 ticks**, measured on 4 slow cores as the min-spec stand-in; record the Phase 0 result (p95 3.5–4.0 ms) | Your pass criterion; the doc didn't say which statistic |
| U5 | §30.2 | Describe the movement **as built**: walks settled inside the hourly tick; drawn walkers are a sample at a visual speed within a detail radius of the camera | At 1× an hour is 83 ms of real time |
| U6 | §12.4 | Replace "Students walk shortest routes" with: **students prefer paved paths and cut across grass only when it saves enough; desire paths form where shortcuts are used regularly, and grass grows back when they aren't** | Your feedback as a current Miami student |
| U7 | §12.4 | **Students leave before the hour** for class; late = arriving after the class-change window | With everyone leaving on the hour, an average 11.9-minute walk always counts as late |
| U8 | §5.1b | Add **Water** as a land state (creeks and ponds can't be built on); keep **ownership as its own layer** alongside the physical land state | The list mixes physical state with ownership; decisions #3–#4 |
| U9 | §5.1, §12.1 | Resolve the rotation conflict: **15° steps** (§12.1 says 90°) | Decision #1 default |
| U10 | §27.5 | Speed hotkeys: **Space = pause, 1–4 = 1×/2×/4×/8×, 5 = skip to next event** | "1–5 speed" doesn't fit 5 states that include pause (decision #2) |
| U11 | §28.1a | Add: kit grid (3 m bays, 3.5 m storeys); **window panes and muntins drawn by a shader**; buildings built by a **procedural kit assembler from per-building recipes** (hero landmarks hand-made); **era variants mostly as shader settings**; rules and budgets in `docs/ART_PIPELINE.md` / `data/art_pipeline.json` | Your Step 4 decisions |
| U12 | §30.1 | **Godot 4.7.2 .NET on .NET 10** (resolves the version `[VERIFY]`) | .NET 8 support ends Nov 2026 |
| U13 | §5.1a, §30.5 | Record the sources actually used: USGS 3DEP 1 m lidar (Ohio statewide 2020–23), OpenStreetMap via Overpass, a campus-centred transverse-Mercator grid; Miami's Historical Timeline, Wikipedia, and the Smith Library walking tour for building dates. OSM has almost no build dates (1 of 3,138) | So the doc matches the pipeline |
| U14 | §30.4 | List the new data files: `timeline.json`, `schedules.json`, `rendering.json`, `art_pipeline.json`, `map/*` (config, land states, land history, generated layers), `spikes/` | Created in Phase 0 |
| U15 | §37 | Phase 0: art spike done as **pipeline prep**, with the Georgian kit + Elliott/generic hall **moved into Phase 1**; outreach **drafted**, sending is on the author. Phase 1: link this report's breakdown and the realistic estimate | Keeps the roadmap honest |
| U16 | §4.1 / §37 | Clarify the Chapter 1 start: **1809 charter map with play starting in 1824**, or play from 1809? (see Q1 below) | §37 says "1809 map"; §4.1 says Chapter 1 is 1824 |

### 7.2 Decisions carried over — defaults in use, please confirm

These are the 12 questions from the outstanding-items doc. None has been changed there, so the working defaults
still apply.

| # | Question | Default in use |
|---|---|---|
| 1 | Rotation step | 15° (`balance.json`) → U9 |
| 2 | Speed hotkeys | Space / 1–4 / 5 reserved → U10 |
| 3 | Water land state | Added → U8 |
| 4 | Physical state vs ownership | Separate ownership layer → U8 |
| 5 | Timeline scope | Miami-owned/used buildings only; no fraternity houses or private homes |
| 6 | Title needs permission? | Title stays locked; asked in the licensing email |
| 7 | Era year boundaries | As in `eras.json` (my reading of §25.1) |
| 8 | Walk model | As built → U5 |
| **9** | **Terrain chunk size** | **400 m, 3 LODs — marked Discuss** → U2 |
| 10 | Undated buildings in past years | Shown only in 2026; a UI toggle shows them in every year |
| 11 | "Present" year | Fixed at 2026 |
| 12 | Daylight saving time | Not modelled (standard time) |

### 7.3 Questions for Phase 1

1. **Chapter 1 start:** does play start in **1824** (classes begin) on a map derived for that year, or in **1809**
   with a fast "before classes" stretch? My recommendation: start in 1824, and use the sandbox's "Empty Hill
   (1809)" start for players who want the 15 years before.
2. **Breakdown and cuts:** OK with the checkpoints 1a–1k and the cuts in §5?
3. **Housekeeping:** add a private remote (GitHub with LFS) and push? Keep committing straight to `main`, or use
   one branch per checkpoint?
4. **The building-dates generator script** (in scratch): commit it to `tools/research/` or drop it? From now on
   `data/timeline.json` is hand-edited.

## Appendix — where things are

| What | Where |
|---|---|
| Project rules, decisions log, findings | `CLAUDE.md` |
| Build, run and test commands | `README.md` |
| Spike A benchmark output | `docs/benchmarks/spike-a-2026-09-30.txt` |
| Terrain comparison | `docs/TERRAIN_COMPARISON.md` |
| Building dates research (177 entries, sources, conflicts, gaps) | `docs/research/BUILDING_DATES.md`, `data/timeline.json` |
| Art pipeline | `docs/ART_PIPELINE.md`, `data/art_pipeline.json` |
| Outreach drafts (not sent) | `docs/outreach/` |
| Map pipeline | `tools/map_pipeline/README.md` |
| Screenshots | `docs/images/` (Spike A, map, terrain, timeline, art import) |
| Outstanding items (living doc) | https://claude.ai/code/artifact/e31b0a24-bb76-4162-b9d7-90c94510838e |
