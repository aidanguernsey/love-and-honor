# LOVE & HONOR — A University Builder
### Game Design Document · v0.4
*A city-builder / management sim where you run Miami University in Oxford, Ohio — from its 1809 charter to the present day and beyond.*

> **How to use this doc:** Every tunable number is marked **[TWEAK]**. Every open design choice is marked **[DECIDE]**. Real-world facts that should be double-checked before shipping are marked **[VERIFY]**. Items settled in review are marked **[LOCKED]**.

---

## Locked Decisions

| # | Topic | Decision |
|---|---|---|
| 1 | Title | **Love & Honor** |
| 2 | Branding | **Real Miami University names, logos, and marks** (pursue permission — see §36) |
| 3 | Art style | **Low-poly 3D** |
| 4 | Platform | **Desktop first** (Windows / macOS / Linux); iPad later |
| 5 | Engine | **Godot 4** (C# for simulation, GDScript for UI/glue) |
| 6 | Map | **Real geography of Oxford, OH**, evolving over time as land is cleared and the university grows, **1809 → present → future** |
| 7 | Tone | **Realistic sim** first, with plenty of nostalgic, fun Miami moments layered on top |
| 8 | Alcohol events | **Green Beer Day and similar are included**, but the game never promotes or rewards underage drinking (see §23.5) |
| 9 | Myaamia | **Partnership track** (see §20) |
| 10 | Academic identity | **Teaching-first**, but **research is real and important** — teacher-scholar model (see §13.7) |
| 11 | Modes | **Campaign + Sandbox + Challenges** as listed in §4 |
| 12 | Utilities | **Simple coverage radius** |
| 13 | Simulation | **Every student and faculty member fully simulated**; only a subset rendered |
| 14 | Audience | **Friends / Miami community + portfolio piece** (non-commercial) |
| 15 | Building placement | **Free placement** by default; real sites are optional suggestions (see §5.1b) |
| 16 | Build tools | **Pure building placement**, no zoning brushes (see §12.2) |
| 17 | Competitor schools | **Real school names** (see §14.2) |
| 18 | Uptown businesses | **Real business names** (see §18.1) |
| 19 | Regional campuses | **Panels only in v1** (see §26) |
| 20 | Endless mode | **Deferred** until the base game is done (see §4.4) |
| 21 | Teaching Identity band | **55–80 sweet spot confirmed** (see §13.7) |

### Changelog
- **v0.4** — Applied the Phase 0 results (`docs/PHASE0_REPORT.md`, updates U1–U16). Terrain: custom chunked mesh generator (open question resolved), 400 m chunks. Walking: paved paths preferred, desire paths only from regular shortcuts, students leave before the hour. Water land state and a separate ownership layer. Rotation in 15° steps. Speed hotkeys. Art: shader-drawn window panes, procedural kit assembler, era variants via shaders. Godot 4.7.2 .NET on .NET 10. Map and date sources as used. New data files. Phase 0 status and the Phase 1 plan; Chapter 1 starts in 1824.
- **v0.3** — Locked free placement, pure building placement, real competitor school names, real Uptown business names, panels-only regional campuses, the Teaching Identity band; deferred Endless mode. Terrain approach still open.
- **v0.2** — Applied all locked decisions. Rewrote Map (§5), Research (§13.7), Green Beer Day (§23.5), Technical Architecture (§30), Save System (§31), Legal (§36), Scope (§37), Open Questions (§38). Added Teacher-Scholar mechanics, land-clearing/evolving-map system, and full-sim agent architecture.
- **v0.1** — Initial draft.

---

## Table of Contents
1. Vision & Pitch
2. Design Pillars
3. Player Role, Goals & Win/Loss
4. Game Modes & Scenarios
5. The Map: Oxford, Ohio
6. Time, Calendar & Pacing
7. Core Loop
8. Resources & Economy
9. Metrics & Ratings
10. Population (Agents)
11. Buildings — Full Catalog
12. Placement, Zoning & Campus Planning
13. Academics System
14. Admissions & Enrollment
15. Student Life
16. Greek Life
17. Athletics
18. Town–Gown (City of Oxford & Uptown)
19. Traditions & Heritage
20. The Myaamia Relationship
21. Sustainability & Utilities
22. Transportation & Parking
23. Events, Crises & Opportunities
24. Policies & Decisions
25. Progression, Unlocks & Research Tree
26. Regional Campuses & Global Presence
27. UI / UX
28. Art Direction
29. Audio
30. Technical Architecture
31. Save System
32. Balancing Formulas
33. Accessibility
34. Tutorial & Onboarding
35. Achievements
36. Legal, Trademark & Sensitivity Notes
37. Scope & Roadmap
38. Open Questions for You

---

## 1. Vision & Pitch

**One-liner:** *SimCity meets Two Point Campus, set on the most beautiful campus that ever was.*

You are the President of Miami University. Starting from a sleepy red-brick college on a hill above the Tallawanda (Four Mile) Creek valley, you build classrooms, residence halls, dining halls, stadiums, and gardens; hire faculty; recruit students; balance a budget squeezed between tuition, the Ohio statehouse, and donors; keep Uptown Oxford happy; and protect the traditions that make Miami *Miami* — all while climbing the national rankings.

**Title:** *Love & Honor* **[LOCKED]**

**Target audience:** Friends and the Miami community — students, alumni ("Miamians," 240,000+ living alumni), families — and anyone who sees it as a portfolio piece. Non-commercial. **[LOCKED]**

**Platform:** Desktop first (Windows/macOS/Linux) built in Godot 4; iPad port later. **[LOCKED]**

**Session length:** 20–60 min sessions; a full campaign ~15–25 hours (longer now that it spans 1809 → today).

**Tone:** **A realistic simulation first.** Systems are grounded in how universities actually work (budgets, state funding, enrollment, maintenance, shared governance). On top of that realism sits a layer of **nostalgia and fun** — traditions, landmarks, Uptown nights, snow on the quad, the tiny stories of individual students. Humor is affectionate and observational, never slapstick that breaks the sim. **[LOCKED]**

---

## 2. Design Pillars

1. **It has to *feel* like Oxford.** Red brick, white trim, cupolas, Slant Walk, the Seal, Uptown on a Friday night, snow on the Formal Gardens. If a Miamian doesn't get nostalgic within 60 seconds, we failed.
2. **Every choice is a trade-off.** A new rec center raises student happiness but also tuition, which hurts affordability, which hurts enrollment from first-gen families. No free lunches.
3. **Students are people, not numbers.** Clickable individuals with names, majors, stories, and a four-year arc from move-in to Commencement.
4. **The campus remembers.** Buildings age, traditions accumulate, alumni come back. Decades of play leave visible history.
5. **Readable systems.** Every metric can be clicked to see *why* it is what it is (SimCity-style overlays + breakdowns).

---

## 3. Player Role, Goals & Win/Loss

### Role
- The player is **the University President** (unnamed, customizable name/portrait). **Not** a real president.
- Answer to the **Board of Trustees** (fictional characters) — they set yearly goals and can fire you.

### Advisors (fictional characters, portrait + voice-lines)
| Advisor | Domain | Personality |
|---|---|---|
| Provost | Academics, faculty, programs | Precise, loves data |
| VP Finance & Business Services | Budget, debt, construction | Worried, always |
| VP Student Life | Housing, health, clubs, safety | Warm, protective |
| VP Enrollment Management | Admissions, aid, marketing | Salesy, optimistic |
| Athletic Director | RedHawks, facilities, TV deals | Hype-man |
| University Architect | Style, planning, maintenance | Purist about Georgian brick |
| Director of Sustainability | Energy, carbon, green space | Earnest |
| Mayor of Oxford (external) | Town–gown | Friendly but firm |
| Student Body President | Student voice | Changes every year |
| Myaamia Center Director (external partner) | Heritage relationship | See §20 — written with care |

### Win conditions (campaign) **[TWEAK]**
- Reach **Top 25 Public University** ranking.
- Maintain **4-year graduation rate ≥ 75%**.
- **Balanced budget** 5 years running.
- **Carbon neutral by 2040** (in-game year; sustainability victory).
- Finish campaign scenario objectives.

### Loss / fail states
- Trustees' **Confidence** hits 0 → you're fired (game over, with a funny "farewell email" screen).
- **Bankruptcy**: cash < –$50M for 2 consecutive years **[TWEAK]** → state takeover.
- **Accreditation lost**: Academic Quality < 20 for 3 years → HLC probation → loss.
- Sandbox mode: no loss conditions unless toggled.

### Trustee Confidence (0–100)
Rises with meeting yearly goals, good press, balanced budgets. Falls with scandals, deficits, missed goals, big enrollment drops. Starts at 60 **[TWEAK]**.

---

## 4. Game Modes & Scenarios

### 4.1 Campaign ("The Presidency")
Linked scenarios, each a chapter of Miami history (loosely — historically *inspired*, not a textbook).

| # | Chapter | Era | Starting state | Key objective |
|---|---|---|---|---|
| 1 | **The Hill** | Play starts in 1824 (classes begin) **[VERIFY]**, on the map as it stood that year | Empty land, one building (Old Main site), tiny budget | Reach 250 students, build first residence hall (Elliott-style) |
| 2 | **Mother of Fraternities** | 1830s–1850s | Small college | Grow student life; found the "Miami Triad" chapters |
| 3 | **Closed & Reopened** | 1873–1885 | University closed for financial trouble **[VERIFY]** | Rebuild enrollment from zero after reopening |
| 4 | **Cradle of Coaches** | 1900s–1950s | Growing campus | Build an athletics program that produces legendary coaches |
| 5 | **Western Merger** | 1974 | Western College for Women joins | Integrate a second campus; preserve its character |
| 6 | **The RedHawks** | 1997 | Name change | Rebrand athletics respectfully, partner with the Miami Tribe |
| 7 | **Building Boom** | 2010s–2020s | Modern campus | Armstrong Student Center, Farmer School, McVey Data Science, Clinical Health Sciences |
| 8 | **The Cliff** | 2026+ | Present-day campus | Survive the national demographic "enrollment cliff" and flat state funding |
| 9 | **2040** | Future | Your campus | Carbon neutrality + Top 25 |

### 4.2 Sandbox
- Choose start: **Empty Hill (1809 charter)**, **Historic 1900**, **Modern 2026 (full real campus)**.
- Toggles: unlimited money, disasters on/off, no Trustees, instant construction.

### 4.3 Challenge Scenarios (standalone, 30–60 min)
- **Snowpocalypse Week:** 30 inches of snow during finals.
- **Budget Cut:** State slashes funding 20% mid-year.
- **National Championship Run:** Hockey makes the Frozen Four — can you handle the crowds?
- **Housing Crunch:** 4,500 first-years confirmed, 3,900 beds.
- **Parking Wars:** Build zero new parking lots. Good luck.
- **Green Beer Day:** Keep everyone safe for 24 hours (see §23.5a).
- **Homecoming Weekend:** Max alumni donations in 3 days.

### 4.4 Endless / Legacy Mode **[DEFERRED]**
On hold until the base game (campaign through 2040 + sandbox + challenges) is finished. Sandbox games can still continue past 2040 with no new content. Original idea for later: play 100+ years forward; buildings age, get renovated or demolished; alumni generations stack up.

---

## 5. The Map: Oxford, Ohio

### 5.1 Map style **[LOCKED]**
- **Low-poly 3D** terrain built from **real elevation data** of Oxford, Ohio and the surrounding Four Mile (Tallawanda) Creek valley.
- Free 3D camera (orbit, pan, zoom, tilt) with a snap-to-grid build layer draped over the terrain.
- Map extent: roughly **4 km × 4 km** centered on campus **[TWEAK]** — covers the original college land, the Mile Square town, Western Campus, the athletics district, natural areas, and farmland buffer.
- Build grid: **1 tile = 10 m** → ~400 × 400 tiles **[TWEAK]**. Buildings snap to grid but can rotate in 15° steps (real campus isn't perfectly axis-aligned).
- Terrain: gentle ridges, creek ravines, woodland, farmland at edges (cornfields — "the Oxford bubble").

### 5.1a Real-world data sources (to build the map)
| Layer | Source | Use |
|---|---|---|
| Elevation | USGS 3DEP **1 m lidar DEM** (Ohio statewide collection, 2020–23), resampled to 5 m | Heightmap → terrain mesh |
| Modern footprints, roads, paths | OpenStreetMap (via the Overpass API) | Modern-era layout, road network. OSM has almost no build dates (1 of 3,138 buildings), so it can't supply the timeline |
| Historic layouts | Sanborn fire insurance maps (Library of Congress), Miami's Walter Havighurst Special Collections, historic campus plans & aerial photos **[VERIFY availability]** | Era-accurate building placements |
| Building dates | Miami's Historical Timeline (miamioh.edu), Wikipedia, the Smith Library's *Walking Tour of Oxford's University Historic District*, Miami news releases; University Archives still to confirm (`docs/research/BUILDING_DATES.md`) | Construction & demolition timeline |
| Waterways | USGS NHD | Creeks, Western lake |
| Land cover (historic) | Period accounts, surveys | 1809 forest / farmland mix |

All map data is baked into Godot resources by an offline **import pipeline** (see §30.5), not loaded live. Coordinates use a transverse-Mercator grid centred on campus, so grid north is true north at the centre.

### 5.1b Evolving map: 1809 → present → future **[LOCKED]**
The map is the *same real place* across the whole game, but it **changes over time**:

- **Land states** (per tile): `Old-growth forest → Cleared / Pasture → Farmland → Town-owned → University-owned → Developed → (Protected natural area)`, plus **Water** (creeks, ponds, Western's lake), which can't be built on.
- **Ownership** (town / university / private) is kept as its **own layer** next to the physical land state, so a tile can be, for example, university-owned forest.
- **1809 start:** mostly forest and scattered farms; the Mile Square town is platted but tiny; the university owns only its original land grant area **[VERIFY]**.
- **Clearing land:** the player spends money + time (and some Sustainability/Beauty) to clear forest; clearing is slower in winter. Clearing can be reversed later by replanting (costly, slow — trees take in-game decades to mature).
- **Acquiring land:** buy adjacent farmland or town parcels at era-appropriate prices; the town grows independently (driven by the university's size), so land near campus gets more expensive over time.
- **Town growth sim:** Oxford (the town) expands on its own — houses, High Street storefronts, roads — responding to student and faculty population. Uptown in 1850 looks nothing like Uptown in 2026.
- **Roads & infrastructure** evolve by era: dirt roads → gravel → brick streets → asphalt; railroad arrives; US-27 later.
- **Historical "ghost" layer (toggle):** shows where real historic buildings stood/stand in each era, so players can follow history or diverge from it.
- **Free placement [LOCKED]:** the player places any building anywhere the land allows, in every mode. Real history is a guide, not a rule:
  - Real buildings are available as **Heritage Projects** from roughly their real dates; the player decides when, whether, and where to build them.
  - Each Heritage Project shows its **real site as an optional ghost outline**. Building it there gives a small Heritage bonus; building elsewhere is completely fine.
  - Era unlocks, prices, and technology still follow the real timeline.
- **Future (post-2026):** map continues; new land states possible (solar fields, geothermal well fields, rewilded natural areas).
- **Time-lapse replay:** at any time, scrub a timeline to watch your campus grow from 1809 — a signature feature (and great for sharing / portfolio).

### 5.2 Districts (real-world inspired, recognizable but stylized)

| District | Real inspiration | Character | Gameplay role |
|---|---|---|---|
| **Academic Quad / Central Campus** | Upham, Harrison, King Library, Irvin, the Seal | Historic core | Highest Heritage value; strict style rules |
| **Slant Walk** | The famous diagonal path | Desire path turned tradition | Special pathing landmark (see §12.4) |
| **North Quad** | Residence halls north of campus | Traditional brick dorms | Housing district |
| **South Quad** | Southern halls, athletics approach | Mixed housing | Housing + dining |
| **East Quad** | Eastern halls | Housing | Housing |
| **Central Quad** | Armstrong Student Center area | Student life hub | Social / dining |
| **Western Campus** | Former Western College for Women | Rolling, wooded, different architecture, lake | Unique style zone, Freedom Summer memorial, sustainability park / solar |
| **Formal Gardens** | The Formal Gardens | Botanical showpiece | Beauty / Heritage / "photo spot" |
| **Athletics District** | Yager Stadium, Goggin Ice Center, Millett Hall, baseball/softball fields | Big venues | Athletics revenue, game-day traffic |
| **Tallawanda / North Campus** | McVey Data Science, science buildings | Modern STEM | Research |
| **Farmer / Business Corridor** | Farmer School of Business | Grand modern-Georgian | Business college |
| **Health Sciences** | Clinical Health Sciences & Wellness | Modern | Health, nursing, counseling |
| **Uptown Oxford** | High Street, bars, restaurants, shops | Town-owned, not yours | Town–gown zone (see §18) |
| **Mile Square / Neighborhoods** | Off-campus student housing, family homes | Town-owned | Off-campus housing market |
| **Chestnut Street** | New transit hub **[VERIFY status]** | Transportation | Bus / multimodal hub |
| **Natural Areas** | Miami's preserved natural areas & trails | Woods, creek | Sustainability, recreation, can't build on (or can, at a big cost) |

### 5.3 Map ownership
- **University land** (buildable).
- **City land** (Uptown, neighborhoods): can't build; can **buy parcels** at high cost + Town Relations penalty.
- **Protected land** (natural areas, historic sites): building requires a Trustee vote and costs Heritage/Sustainability.
- **Farmland edge**: cheap to acquire, far from core (long walk times).

### 5.4 Real-landmark placement **[LOCKED]**
- **Modern (2026) sandbox & later campaign chapters:** the real campus is pre-placed at real locations; the player adds, renovates, or replaces.
- **Early campaign / Empty Hill sandbox:** real buildings are **Heritage Projects** that the player places freely (§5.1b); the real site is shown as an optional ghost outline with a small Heritage bonus.
- Landmarks tied to specific ground (the Formal Gardens, Western lake, creek ravines) can only exist where the real geography supports them.

---

## 6. Time, Calendar & Pacing

### 6.1 Time scale
- **1 in-game day = 2 real seconds** at normal speed **[TWEAK]**.
- Speeds: Pause, 1×, 2×, 4×, 8× (skip-to-next-event button).
- **1 academic year ≈ 12–15 real minutes** at 1×.

### 6.2 Academic calendar (modeled on Miami's real rhythm)
| Period | Approx. dates | Gameplay |
|---|---|---|
| Summer | May–mid Aug | Construction season (build speed ×1.5), orientation, summer camps revenue, low population |
| **Move-In & Welcome Weekend** | Late Aug | Traffic spike; first impressions → first-year happiness |
| **Love & Honor Convocation** | Late Aug | Ceremony event; sets class morale |
| Fall Semester | Late Aug–mid Dec | Classes, football season, Homecoming |
| **Homecoming** | Oct | Alumni visits, donations spike |
| Thanksgiving break | Late Nov | Campus empties briefly |
| Finals | Mid Dec | Library/study space demand ×3, stress ↑ |
| **Winter Term** (optional) | January | Small enrollment, study abroad, cheap revenue |
| Spring Semester | Late Jan–early May | Classes, hockey/basketball peak |
| Spring break | March | |
| **Spring Commencement** | May | Graduation event; grads become Alumni |
| Budget cycle | Fiscal year July 1 | Annual budget screen |
| Admissions cycle | Nov–May | Applications → admits → deposits (May 1) |

### 6.3 Seasons & weather
- **Fall:** foliage (big beauty bonus), football.
- **Winter:** snow — requires snow removal budget; ice → injuries; snow days possible. Visual: snow on cupolas.
- **Spring:** Formal Gardens bloom (beauty ×1.5), rain → mud on unpaved desire paths.
- **Summer:** heat → A/C energy costs.
- Random weather events: thunderstorms, tornado watch (rare), polar vortex, heat wave.

---

## 7. Core Loop

**Moment-to-moment (seconds):** Place/zone buildings, draw paths, inspect students, respond to pop-ups.

**Semester (minutes):** Watch demand bars (Housing / Classroom / Dining / Social / Study / Parking), fix bottlenecks, handle 3–6 events.

**Year (10–15 min):** Budget → Admissions → Hire faculty → Set tuition & policies → Rankings released → Trustee review → Commencement.

**Decade (hours):** Major capital projects, reputation growth, traditions forming, alumni giving compounding, campaigns (fundraising drives).

```
Build → Students arrive → Needs & demands → Happiness & learning
   ↑                                              ↓
Money ← Tuition/State/Donors ← Reputation ← Graduation & outcomes
```

---

## 8. Resources & Economy

### 8.1 Currencies
| Resource | Description |
|---|---|
| **$ Operating Cash** | Day-to-day money |
| **Endowment** | Long-term fund; yields ~4.5%/yr to operating budget **[TWEAK]**; can't spend principal (except emergency, huge Trustee penalty) |
| **Capital / Bonds** | Borrow for construction; debt service each year; credit rating affects interest |
| **Political Capital** | Earned with Trustees/state; spent on controversial decisions (raise tuition, cut programs, demolish historic buildings) |
| **Heritage** | Accumulated from traditions and historic buildings; unlocks prestige, alumni giving multipliers |

### 8.2 Revenue sources **[TWEAK all]**
| Source | Starting (Modern mode, per year) | Driver |
|---|---|---|
| Tuition — in-state | ~$17k/student | # Ohio students × rate × (1 – discount rate) |
| Tuition — out-of-state | ~$40k/student | # non-Ohio × rate |
| Tuition — international | ~$40k/student | # intl |
| Tuition — graduate | varies by program | # grads |
| **State Share of Instruction** | ~$70M | Completions-based formula (Ohio SSI) — rewards graduation, not just enrollment |
| Room & Board | ~$15k/resident student | Beds filled × rate |
| Dining plans | included/extra | Dining quality |
| Athletics | Tickets, TV/conference share, sponsorships | Wins, venue quality |
| Donations | Alumni + major donors | Alumni happiness, Heritage, fundraising staff |
| Research grants | Federal/state/private | Faculty research score, lab space |
| Endowment yield | ~4.5% of endowment | Market events |
| Auxiliary | Conferences, summer camps, parking permits, bookstore | Facilities |
| Regional campuses | Net positive/negative | See §26 |

> **Note:** Real figures change yearly — treat these as *balance numbers*, not facts. **[VERIFY]** if you want realism.

### 8.3 Expenses
| Category | Driver |
|---|---|
| Faculty salaries | # faculty × rank × market rate |
| Staff salaries | # staff |
| Benefits | ~30% of salaries **[TWEAK]** |
| Building maintenance | Per-building upkeep × age multiplier |
| **Deferred maintenance backlog** | Skipped maintenance accumulates → breakdowns, closures |
| Utilities | Energy use × fuel type (steam/geothermal/solar) |
| Financial aid / scholarships | Discount rate policy |
| Debt service | Bonds |
| Athletics subsidy | Usually negative for mid-major programs |
| Snow removal, security, IT, insurance | Flat + scale |
| Marketing / admissions | Player-set |

### 8.4 Tuition model
- **Miami Tuition Promise–style cohort pricing** **[VERIFY name/details]**: tuition locked for 4 years per entering class. Player sets the new-cohort rate; can't change existing cohorts.
- Ohio caps in-state tuition increases (state policy variable, can change via events) **[TWEAK: 3%/yr default cap]**.
- Out-of-state tuition unlimited but elastic (applications drop).
- **Tuition reciprocity (Indiana counties)** as an unlockable policy — lowers price for nearby Indiana students, boosts applications **[VERIFY]**.

### 8.5 Construction financing
- **Cash**: pay upfront.
- **Bonds**: 20–30 year terms, interest 3–7% by credit rating **[TWEAK]**.
- **Donor-named buildings**: large donors fund 30–100% in exchange for naming rights (randomized fictional donor names; player picks).
- **Public-private partnership**: e.g., leased housing (like a developer-built hall) — no upfront cost, lower revenue share.

### 8.6 Credit rating
AAA → B tiers, based on debt-to-revenue, reserves, enrollment trend. Affects bond interest.

---

## 9. Metrics & Ratings

All 0–100 unless noted. Each has an overlay map and a clickable breakdown.

| Metric | Inputs | Effects |
|---|---|---|
| **Reputation / Ranking** (rank #1–#400) | Academic Quality, grad rate, selectivity, faculty research, alumni giving, peer perception | Applications, donations, faculty recruiting |
| **Academic Quality** | Student:faculty ratio, faculty quality, class sizes, facilities, library | Learning, grad rate, accreditation |
| **Student Happiness** | Needs satisfaction (see §10) | Retention, word of mouth, alumni giving later |
| **Retention (1st→2nd year %)** | Happiness, academic support, finances, belonging | Enrollment, ranking |
| **4-Year Graduation Rate** | Retention, advising, course availability | Ranking, State Share |
| **Affordability** | Net price vs. family income | Access, first-gen enrollment, state politics |
| **Access / Diversity** | First-gen %, Pell %, students of color %, geographic spread | Ranking components, grants, campus climate |
| **Town Relations** | See §18 | City cooperation, zoning approvals |
| **Beauty** | Green space, trees, gardens, architecture consistency, cleanliness | Happiness, admissions visits conversion |
| **Heritage** | Traditions active, historic buildings preserved, landmarks | Alumni giving multiplier, Trustee confidence |
| **Safety** | Police, lighting, emergency phones, alcohol policy | Happiness, parents' trust |
| **Health & Wellness** | Counseling capacity, health center, rec facilities | Retention, crisis prevention |
| **Sustainability** | Carbon emissions (tons CO₂e), renewables %, green buildings | 2040 goal, student happiness (small), grants |
| **Athletics Prestige** | Wins, facilities, coaches | Donations, applications (small), game-day revenue |
| **Walkability** | Average class-change walk time | Late arrivals → learning ↓; happiness |
| **Teaching Identity** | Teaching loads, class sizes, full-time faculty %, undergrad research, research intensity | Sweet-spot bonuses; see §13.7 |
| **Research Output** | Publications, creative works, grants, undergrad research participation | Reputation, faculty retention, revenue, grad programs |
| **Trustee Confidence** | See §3 | Job security |

### Rankings formula (simplified, US News–inspired, **[TWEAK]** weights)
- Outcomes (grad rate, retention, social mobility): 40%
- Faculty resources: 20%
- Peer assessment (lagging average of Reputation): 20%
- Financial resources per student: 10%
- Student excellence (selectivity): 7%
- Alumni giving: 3%
Released each **September** as a big event pop-up.

---

## 10. Population (Agents)

### 10.1 Students
**Every student is fully simulated as an individual agent** — no cohort aggregation **[LOCKED]**. Target capacity: **~30,000 students + ~2,500 faculty + staff roles** (headroom above the modern Oxford campus's ~19,000–23,000 students) **[TWEAK]**. Only a **rendered subset** (~1,500–3,000 low-poly figures, chosen by camera view and "followed" students) is drawn; everyone else keeps living their schedule in the simulation. See §30.2 for how this is achieved.

**Attributes:**
- Name (generated), hometown (Ohio county / state / country), portrait
- Year (1st–4th, 5th+, grad)
- College & Major
- GPA (0.0–4.0)
- Academic preparation (incoming)
- Family income bracket; first-gen flag; Pell flag
- Residency: in-state / out-of-state / international
- Housing: on-campus hall / Greek house / off-campus Mile Square / commuter
- Affiliations: Greek chapter, clubs, athlete, band, honors
- Personality traits (2 of): Studious, Social, Athletic, Artsy, Activist, Homesick, Night Owl, Early Bird, Outdoorsy, Foodie, Entrepreneur
- **Needs (0–100):** Sleep, Food, Study, Social, Fun, Health, Money, Belonging, Safety, Commute
- Happiness (weighted needs)
- Stress
- Story log (key life events: "Joined Beta Theta Pi", "Kissed under Upham arch at midnight", "Stepped on the Seal (!)", "Switched from Pre-Med to Marketing")

**Lifecycle:** Applicant → Admit → Deposit → Move-in → Semesters → (Transfer out / Stop out / Graduate) → Alumnus.

**Daily schedule:** Wake → dining → classes (pathing between buildings on schedule) → study → social → Uptown (evenings, esp. Thu–Sat) → sleep.

### 10.2 Faculty
**Every faculty member is individually simulated** (same engine as students) **[LOCKED]**.
- Rank: Lecturer / Assistant / Associate / Full Professor / Distinguished
- Tenure status
- **Teaching score, Research score, Mentoring score, Service** (the teacher-scholar profile, see §13.7)
- Research focus (field, active projects, grants held, undergrad researchers supervised)
- Daily schedule: teaching, office hours, lab/studio time, committee meetings (yes, really)
- Department
- Salary, satisfaction, poaching risk (other schools recruit your stars)
- Special: **Legendary Professor** (rare; huge department boost, students tell stories about them)

### 10.3 Staff
Abstracted into departments: Facilities, Dining, Housing, IT, Police, Advising, Counseling, Admissions, Advancement (fundraising), Athletics. Budget slider per department → service level.

### 10.4 Alumni
- Generated from graduates; persist forever.
- Attributes: grad year, major, career success tier, loyalty (from student happiness), wealth.
- Give annually (probability × capacity); some become **Major Donors** (named-building offers), **Trustees**, famous alumni (random fun news: "Class of '31 alum becomes CEO").
- Return for Homecoming / reunions.

### 10.5 Parents
Aggregate "Parent Sentiment" — affected by safety, cost, communication, move-in experience. Affects yield.

### 10.6 Oxford residents ("Townies")
Aggregate + a few named characters (café owner, landlord, city council member). Care about noise, parking, trash, taxes, business revenue.

### 10.7 Prospective students
Tour groups visible walking campus (in admissions season); conversion affected by Beauty along the tour route (player can draw the official tour route!).

---

## 11. Buildings — Full Catalog

Format: **Name** — footprint (tiles) · cost · upkeep/yr · capacity · effects · unlock. All values **[TWEAK]**.

### 11.1 Academic Buildings
Each academic building is assigned to a **College** and one or more **Departments**.

| Building | Footprint | Cost | Upkeep | Capacity | Effects | Unlock |
|---|---|---|---|---|---|---|
| Small Classroom Hall | 4×4 | $8M | $250k | 600 seats | Classroom supply | Start |
| Large Classroom Hall | 6×6 | $25M | $700k | 1,800 seats | Classroom supply | Start |
| Lecture Hall Auditorium | 5×5 | $15M | $400k | 400 seats/room | Big intro courses; learning –5% vs small rooms | Start |
| Science Lab Building | 6×6 | $60M | $1.8M | 400 lab seats | Required for STEM majors; research +10 | 1900 / Modern start |
| Research Center | 6×8 | $90M | $2.5M | 60 faculty labs | Grants ×1.5 for dept | Research tree |
| Business School (Farmer-style) | 8×8 | $80M | $2M | 2,500 seats | Business college prestige; donor magnet | Modern |
| Engineering & Computing Center | 8×6 | $70M | $2M | | CEC programs | Modern |
| Data Science Center (McVey-style) | 6×6 | $58M | $1.5M | | Statistics, CS; "Analytics" research branch | Modern |
| Education Building (McGuffey-style) | 6×6 | $30M | $900k | | EHS college; historic if pre-1920 | 1900 |
| Health Sciences & Wellness Complex | 10×8 | $96M | $3M | | Nursing, PA, speech path; **also** counseling + health center | Modern |
| Fine Arts Center | 6×6 | $40M | $1.2M | | CCA; culture events | 1900 |
| Architecture & Interior Design Studio | 5×5 | $25M | $700k | | Arch program; Beauty +2 nearby | 1950 |
| Performing Arts Hall (Hall Auditorium-style) | 6×5 | $35M | $900k | 1,000 seats | Concerts, lectures; revenue | 1900 |
| Humanities Hub (Bachelor Hall-style renovation) | 6×6 | $40M | $1M | | Humanities programs, TV studio add-on | Modern |
| Honors College House | 4×4 | $12M | $300k | | Top applicants ↑ **[VERIFY Honors College]** | Policy |
| Graduate School Center | 4×4 | $15M | $400k | | Grad enrollment cap ↑ | 1950 |
| Observatory | 3×3 | $5M | $100k | | Physics/astro; fun +small; night event | 1900 |
| Greenhouse / Botanical Conservatory | 4×3 | $6M | $150k | | Biology; Beauty | 1900 |
| Art Museum | 5×5 | $20M | $500k | | Culture, Heritage, visitors | 1950 |
| Myaamia Center / Classroom | 3×3 | $5M | $150k | | See §20 | Partnership |

### 11.2 Libraries & Study
| Building | Notes |
|---|---|
| **Main Library (King-style)** | Study need +++; 24/7 upgrade during finals |
| Branch Library — Science/Engineering (BEST-style) | STEM study |
| Branch Library — Art & Architecture (Wertz-style) | Arts study |
| Study Lounge (small, placeable inside halls) | Local study |
| Makerspace / Innovation Lab | Entrepreneurship, Engineering |
| Special Collections Wing | Heritage, research |

### 11.3 Residence Halls
All halls have a **Style**, **Age**, **Condition**, **Amenities**, **Room types**.

| Hall type | Footprint | Beds | Cost | Notes |
|---|---|---|---|---|
| Historic Hall (Elliott/Stoddard-style) | 3×4 | 60–120 | Can't build new — landmark only | Oldest halls; Heritage ++, amenities – |
| Traditional Corridor Hall | 4×6 | 250 | $30M | Doubles, shared baths; cheap; social +; happiness – |
| Suite-Style Hall | 5×6 | 350 | $55M | Privacy +; pricier room rate |
| Apartment-Style (upperclass) | 6×6 | 400 | $70M | Keeps juniors/seniors on campus; competes with off-campus market |
| Living-Learning Community Hall | 4×6 | 250 | $40M | Themed (Honors, Global, Arts, Wellness, Entrepreneurship, Leadership) → retention +5% |
| Leased Private Hall | 6×6 | 500 | $0 upfront | P3 partnership, revenue share 30% **[TWEAK]** |
| Western Campus Hall | 4×6 | 250 | $45M | Western-style architecture |

**Upgrades:** A/C (big for summer & early fall), renovation (resets age), kitchens, study lounges, gender-inclusive floors, accessibility retrofit, geothermal hookup.

**Housing policy:** Require first- and second-years on campus (real Miami-style policy) **[VERIFY]**. Toggle.

### 11.4 Dining
| Building | Effects |
|---|---|
| Dining Commons (buffet) | Food need, social |
| Market / Food Hall | Variety +; higher cost |
| Café / Grab-and-Go | Small footprint, near academic buildings |
| Late-Night Dining | Night owls ++; safety + (keeps students out of Uptown late? small) |
| Food Truck Spot | Cheap, event-driven |
| Specialty: Kosher/Halal/Vegan station (upgrades) | Access & belonging |
| Farm-to-table upgrade | Sustainability +, cost + |

### 11.5 Student Life
| Building | Effects |
|---|---|
| **Student Center (Armstrong-style)** | Social ++, dining, clubs, events; huge central hub |
| Recreation Center | Health ++, fun |
| Outdoor Rec / Climbing Wall / Pools (upgrades) | |
| Intramural Fields | Fun, health; cheap, space-hungry |
| Counseling Center | Mental health capacity (wait times!) |
| Student Health Center | Illness recovery; flu season |
| Career Center | Outcomes ++ → Reputation |
| Multicultural Center | Belonging for students of color; access |
| Interfaith Center / Chapel | Belonging; weddings revenue (small) |
| Veterans & ROTC Center | ROTC programs |
| Club Sports Complex | |
| Bookstore | Revenue |
| Campus Radio / Student Newspaper Office | "The Miami Student"–style paper (see note §36); news ticker events, transparency |
| Theater Black Box | Arts |
| Outdoor Amphitheater | Events, concerts |
| Bike Shop / Rental Stand | Transportation |

### 11.6 Athletics
| Building | Real inspiration | Effects |
|---|---|---|
| Football Stadium | Yager Stadium | Football; game-day revenue; Homecoming |
| Ice Arena | Goggin Ice Center | Hockey (big at Miami), public skating |
| Basketball / Volleyball Arena | Millett Hall → new arena project (targeted ~2028) **[VERIFY]** | Basketball, commencement, concerts, career fairs |
| Baseball Field | McKie Field at Hayden Park **[VERIFY]** | |
| Softball Field | | |
| Soccer / Track Complex | | |
| Indoor Practice Facility | Recruiting ↑ | |
| Athletic Performance Center | Wins ↑ | |
| Hall of Fame / Cradle of Coaches Plaza | Heritage, alumni | |
| Tailgate Lots | Game-day fun, parking | |

### 11.7 Administration & Operations
| Building | Effects |
|---|---|
| Administration Building (Roudebush-style) | Required; bureaucracy efficiency |
| Admissions & Visitor Center | Tour conversion ++ |
| Alumni Center | Donations ++, Homecoming |
| Police / Public Safety Station | Safety; response time radius |
| Facilities / Physical Plant | Maintenance speed; required per N buildings |
| Steam / Power Plant | Energy (coal → gas → geothermal, see §21) |
| Geothermal Plant | Clean heating/cooling |
| Solar Field | Clean electricity; needs land (Western Campus fits) |
| Chiller Plant | Cooling |
| Water Tower | Required |
| IT Data Center | Online learning, research computing |
| Mailroom / Package Center | Small happiness (packages are a real student need, lol) |
| Parking Lot / Garage | See §22 |
| Transit Hub (Chestnut Street–style) | See §22 |
| Snow Removal Depot | Winter operations |
| Recycling / Compost Center | Sustainability |

### 11.8 Landscape & Decoration
| Item | Effects |
|---|---|
| Trees (oak, maple, sycamore, ginkgo) | Beauty; shade; fall color |
| Lawn / Quad | Beauty, social (frisbee/sunbathing) |
| Brick Path (standard) | Walk speed 1.0; Beauty + |
| Concrete Path | Cheaper; Beauty – ; Architect grumbles |
| Gravel / Dirt (desire path) | Auto-created; mud in rain |
| Benches, Lamps (historic style), Bike racks | |
| Fountains | |
| Flower Beds | Seasonal |
| Statues (generic/fictional figures, e.g., "The Scholar") | |
| Memorials | |
| Sundial, Bell, Gazebo | |
| Hammocks (seasonal) | Students love them |
| Snow sculptures (winter event) | |

### 11.9 Landmarks / Wonders (unique, one each)
Each gives a large area Beauty + Heritage aura and a unique effect. Landmarks use their **real names** **[LOCKED]** (covered by the branding request in §36).

| Landmark | Unique effect |
|---|---|
| **The Seal** (on the Academic Quad) | Tradition: students who step on it "won't graduate" — generates daily comedic events; Heritage +++ |
| **Upham Hall Arch** | "Miami Merger" tradition (couples kissing under the arch at midnight are said to marry) → later alumni couples ("Miami Mergers") give 2× donations |
| **Slant Walk** | See §12.4 |
| **Formal Gardens** | Beauty +++; spring bloom event; wedding photos |
| **King Library** | Study aura; 24/7 finals mode |
| **Sesquicentennial Chapel** | Belonging; weddings; quiet space |
| **Kumler Chapel** (Western) | Western Heritage |
| **Freedom Summer Memorial** (Western) | Honors 1964 Freedom Summer volunteer training at Western College; civic engagement +, Heritage +; interactive history plaque |
| **Beta Bells** (bell tower/carillon) **[VERIFY location/name]** | Chimes on the hour; happiness aura |
| **Harrison Hall** | Admin-historic core |
| **Western Lake / Peabody area** | Scenic |
| **Yager Stadium bowl** | Game-day hype aura |
| **Goggin Ice Center** | Hockey hype |
| **Cradle of Coaches Plaza** | Athletics prestige +, coach recruiting |
| **Hall Auditorium** | Culture |

---

## 12. Placement, Zoning & Campus Planning

### 12.1 Build mode
- Freeform building placement on grid; rotate in 15° steps (§5.1).
- Buildings need **path access** (entrance tile adjacent to a path).
- **Construction time:** Small 3 months, Medium 9 months, Large 18–24 months in-game **[TWEAK]**; summer ×1.5 speed; winter ×0.7.
- Construction zones cause noise (–happiness nearby), block paths (detours!), cranes visible.

### 12.2 Pure building placement **[LOCKED]**
No zoning brushes. The player places every building directly. Districts (§5.2) are just named areas used for Style Harmony, overlays, and advisor comments; they emerge from what the player builds rather than being painted.

### 12.3 Architecture Style System (signature mechanic)
- Every building has a **Style**: *Georgian Revival* (the Miami look: red brick, white trim, columns, cupolas, slate roofs), *Western Collegiate*, *Mid-Century Modern*, *Brutalist*, *Contemporary Glass*.
- **Style Harmony** score per district: matching Georgian = Beauty +, Heritage +; clashing = Beauty –, Architect complaint, alumni letters to the editor.
- Georgian costs ~15% more **[TWEAK]**.
- Fun event: "A donor insists on a glass box." Accept (money) or refuse (heritage).
- Historic buildings (pre-1920) can be **Renovated** (keeps heritage, modernizes) or **Demolished** (big Heritage penalty, protest event).

### 12.4 Pathfinding & the Slant Walk mechanic
- Students **prefer paved paths** and cut across grass only when it saves enough time (a long diagonal across a quad is worth it; trimming a corner isn't).
- Where a shortcut is used **regularly**, the grass is matted down → a **desire path** forms (grass wears to dirt over weeks). Occasional crossings leave no mark, and grass grows back when a shortcut stops being used. Desire paths exist but are the exception: most walking is on paved paths.
- Player can **pave** a desire path → becomes a brick path; the first major diagonal desire path through the core quad can be designated **the Slant Walk** landmark (Heritage +, unique).
- Walk time matters: 10-minute class change window **[TWEAK]**. Students **leave before the hour** based on their expected walk; a student is late if they arrive after the class-change window → learning –.

### 12.5 Adjacency bonuses/penalties **[TWEAK]**
| Pair | Effect |
|---|---|
| Residence hall ↔ Dining within 6 tiles | Food need met faster |
| Academic ↔ Library within 10 | Study + |
| Anything ↔ Green space | Beauty + |
| Residence hall ↔ Stadium within 8 | Noise – on game days |
| Residence hall ↔ Power plant | Happiness – |
| Research center ↔ Research center | Research cluster + |
| Uptown ↔ Campus edge | Town business revenue ↑, noise complaints ↑ |

### 12.6 Overlays
Happiness, Beauty, Heritage, Walk time, Noise, Safety, Energy use, Building condition, Classroom utilization, Parking demand, Desire paths, Snow coverage, Wi-Fi coverage (joke-but-real need), Style harmony.

### 12.7 Demand bars (like SimCity RCI)
**Beds · Seats · Labs · Dining · Study · Social · Rec · Parking · Counseling**

---

## 13. Academics System

### 13.1 Colleges (Oxford campus, real structure) **[VERIFY current names]**
- College of Arts & Science (CAS)
- Farmer School of Business (FSB)
- College of Creative Arts (CCA)
- College of Education, Health & Society (EHS)
- College of Engineering & Computing (CEC)
- Graduate School
- Honors College
- (Regional) College of Liberal Arts & Applied Science — see §26

### 13.2 Departments
Each department has: faculty count, student majors, course demand, required building type, research output, prestige.

Example departments per college (not exhaustive; players can create more):
- **CAS:** Biology, Chemistry & Biochemistry, Physics, Geology, Mathematics, Statistics, Psychology, Economics, Political Science, History, English, Philosophy, Global & Intercultural Studies, Media & Communication, Journalism, Sociology, Anthropology, Languages
- **FSB:** Accountancy, Finance, Marketing, Management, Economics (shared), Entrepreneurship, Information Systems & Analytics
- **CCA:** Architecture & Interior Design, Art, Music, Theatre, Emerging Technology in Business + Design
- **EHS:** Teacher Education, Kinesiology & Nutrition, Nursing, Family Science & Social Work, Speech Pathology & Audiology, Sport Leadership
- **CEC:** Computer Science & Software Engineering, Electrical & Computer Engineering, Mechanical & Manufacturing Engineering, Chemical/Paper Engineering, Bioengineering

### 13.3 Faculty hiring
- Yearly hiring window (Feb–Apr). Candidates generated per department with Teaching/Research/Salary ask/Personality.
- Budget-constrained. Tenure-track vs. lecturer (cheaper, less research, more teaching).
- Tenure review after 6 in-game years → keep or lose.
- Faculty satisfaction: salary, lab/office space, teaching load, leadership. Low → leave.
- Shared governance: **Faculty Senate** vote on big academic changes (can pass/fail).

### 13.4 Programs
- Add new majors (costs, needs faculty & space, takes 2 years to launch; e.g., "AI", "Sustainability Management").
- Cut low-enrollment programs → saves money but Faculty morale –, protest event, press. (Can be forced by a "state mandate" event.)
- Program demand shifts over decades (trend engine: e.g., CS up, some humanities down, nursing up; random "hot major" events).

### 13.5 Learning model
Per student per semester:
`Learning = PrepFactor × FacultyQuality × ClassSizeFactor × FacilityFactor × (1 – Stress penalty) × Attendance`
→ drives GPA → drives graduation probability & outcomes.

### 13.6 Course availability
If seats < demand → students can't get required courses → graduation delayed → 4-yr rate ↓. Classic bottleneck puzzle.

### 13.7 Research — the Teacher-Scholar Model **[LOCKED]**
Miami stays **teaching-first**, but research is a real, important, fully simulated system. The design goal: **research that strengthens teaching**, not research that competes with it. There is no "become a research powerhouse" fork; instead, the player keeps research and teaching in a healthy balance.

#### How research works
- **Who does it:** Faculty with Research skill, time (teaching load leaves room), and space (labs, studios, archives, field sites, computing).
- **Outputs:** Publications, creative works (exhibitions, performances, compositions — arts research counts), patents/inventions, and grants.
- **Funding:** External grants (federal, state, foundations, industry), internal seed grants (player-funded), endowed chairs and research centers (donor-funded).
- **Research clusters:** Faculty in related departments near shared facilities form clusters (e.g., data science, environmental science, health sciences, education, the humanities) → output bonus.
- **Research infrastructure:** labs, core facilities, the Data Science center, field stations and natural areas, special collections, research computing, the Myaamia Center (language & cultural revitalization research, see §20).

#### Why it matters (effects)
| Effect | Detail |
|---|---|
| **Faculty quality & retention** | Active scholars are better teachers and stay longer when supported; starving research → star faculty leave |
| **Undergraduate research** | Signature Miami-style mechanic: students join faculty projects → learning ++, retention +, grad-school/career outcomes ++, alumni loyalty + |
| **Reputation** | Peer assessment rises with scholarly visibility |
| **Revenue** | Grant indirect-cost recovery helps the budget (but grants rarely cover full cost — realistic) |
| **Graduate programs** | Master's & doctoral programs need research-active faculty; grads serve as TAs/research assistants |
| **Community & state** | Applied research (workforce, health, environment, education) → State relations +, Town relations + |
| **Accreditation & program quality** | Some programs need research/scholarship to stay accredited |

#### The balance mechanic: Teaching Identity meter (0–100)
- Computed from: teaching loads, class sizes, % of courses taught by full-time faculty, undergrad research participation, advising quality, research intensity.
- **Sweet spot (55–80) [LOCKED]:** "Teacher-Scholar" — bonuses to learning, retention, reputation, and faculty morale.
- **Too low (research-heavy, <40):** big-lecture classes, less faculty contact → student happiness and grad rate ↓; alumni and Trustees unhappy ("that's not Miami").
- **Too high with little research (>90):** faculty stagnate, top hires decline offers, peer reputation ↓, grants dry up.
- Player levers: teaching-load policy, course releases for research, seed grants, undergrad research funding, hiring criteria (weight teaching vs. research), sabbaticals.

#### Undergraduate research program (signature feature)
- Fund summer scholars, research credit, a campus-wide research forum (spring event), and research-supervising faculty stipends.
- Individual students can be followed through their project: "Joined Dr. X's lab sophomore year → presented at the spring forum → published as a senior → went on to grad school."
- Target stat: **% of graduates with a research/creative experience** — feeds rankings and alumni outcomes.

#### Research classification
- Carnegie-style classification is tracked **as an outcome, not a goal**; Miami's modern-era classification is set at game start **[VERIFY current classification]**. Dramatic shifts in research intensity trigger Trustee and faculty debates rather than automatic wins.

### 13.8 Study abroad
- **Luxembourg center** (see §26) — send students abroad each semester → Global score, happiness +, fewer beds needed that semester.
- Winter Term trips.

---

## 14. Admissions & Enrollment

### 14.1 Cycle
1. **Marketing** (Sep–Nov): Spend on regions (Ohio, Chicago suburbs, Midwest, Northeast, international). Campus visits.
2. **Applications** (Nov–Feb): Pool generated by Reputation, marketing, price, location factors, national demographics.
3. **Admission decisions** (Mar): Player sets **admit rate** or **academic threshold** + holistic weights (test-optional toggle).
4. **Financial aid packaging**: merit vs. need-based budget; discount rate.
5. **Yield** (May 1 deposits): probability per admit (prestige, net price, visit experience, competitor schools).
6. **Summer melt**: some deposited students don't show.
7. **Move-in** (Aug).

### 14.2 Competitor schools **[LOCKED: real names]**
Real schools compete for your applicants: Ohio State, Ohio University (Battle of the Bricks rival), University of Cincinnati, University of Dayton, Indiana, Michigan, Purdue, Kentucky, Xavier, and private Midwest colleges **[TWEAK list]**.
- **Names only:** competitors appear by name in text, charts, and the rankings table; no competitor logos or marks.
- **Simulated simply:** each has a Reputation, Price, size, and regional pull that drift over time; the applicant picks via a utility function.
- **Rankings table** shows your rank among real schools; competitor positions are simulated, not real published rankings.
- Competitors are portrayed neutrally: no scandals, events, or jokes aimed at a specific real school (rivalry trash talk stays at game-day banter level).

### 14.3 Demographic trend
- **Enrollment cliff**: national 18-year-old population declines after ~2025–2026 → applicant pool shrinks yearly **[TWEAK: –1.5%/yr for 10 years]**.
- Counter with: out-of-state recruitment, adult learners (regionals), graduate programs, online, international, transfer pathways.

### 14.4 Class profile targets (Modern mode start, approximate) **[VERIFY/TWEAK]**
- First-year class: ~4,000–4,300 on Oxford campus
- Ohio share: ~50–65%
- First-gen: ~15–20%
- Avg GPA: ~3.8+

### 14.5 Special admits
Athletes (coach requests), Legacies (alumni kids — donors like, access critics don't), Honors scholars, International, Transfer, Pathways/bridge programs from regionals.

---

## 15. Student Life

### 15.1 Needs → Buildings map
| Need | Satisfied by |
|---|---|
| Sleep | Residence halls (quality, noise, A/C) |
| Food | Dining, cafés, Uptown restaurants |
| Study | Library, lounges, quiet spaces, Wi-Fi |
| Social | Student center, quads, Greek life, clubs, Uptown |
| Fun | Rec, events, athletics, Uptown, concerts |
| Health | Rec, health center, counseling, safety |
| Money | Aid, on-campus jobs, low prices |
| Belonging | LLCs, clubs, multicultural center, traditions, chapels |
| Safety | Police, lighting, policies |
| Commute | Walk time, buses, parking (for commuters) |

### 15.2 Clubs & Organizations
- 600+ orgs abstracted as **Club Categories**: Academic, Arts, Cultural, Faith, Service, Sports clubs, Media, Politics, Hobby, Professional.
- Budget via Student Activity Fee (player sets fee; students' Money need –).
- Each category boosts different traits' Belonging.

### 15.3 Campus Events (student-run & university-run)
Welcome Weekend (fireworks!), Convocation, Homecoming parade, Family Weekend, Concerts in the arena, Speaker series, Career Fair, Dance marathon–style philanthropy, Winter lights, Spring festival, Commencement.

### 15.4 Mental health system
- Counseling **wait times** (days) visible stat.
- Stress spikes at midterms/finals.
- Under-capacity → crisis events, retention –.
- Handled seriously: no jokes about self-harm; events framed as resourcing decisions. Crisis events show real-world resource note in an info panel (988 Lifeline) **[LOCKED]**.

### 15.5 Alcohol & safety
- Follows the hard rules in §23.5a: only 21+ agents drink; underage drinking only ever has negative outcomes.
- Uptown's social scene (restaurants, music, late-night food, bars for 21+) contributes to Fun; alcohol-related incidents reduce Safety.
- Policies: medical amnesty, education programs, late-night buses, police presence, alcohol-free late-night programming.
- Balance: heavy crackdown → Fun ↓, Town relations mixed; laissez-faire → incidents ↑.

---

## 16. Greek Life

Miami is nicknamed **"Mother of Fraternities"**.

### 16.1 Heritage chapters (real history)
- **Beta Theta Pi (1839), Phi Delta Theta (1848), Sigma Chi (1855)** — the "Miami Triad" **[VERIFY dates]**
- **Phi Kappa Tau (1906)**, **Delta Zeta (1902)** — also founded at Miami **[VERIFY]**
Heritage chapters give Heritage bonuses; founding them is a Campaign Chapter 2 objective.

### 16.2 Mechanics
- ~⅓ of undergrads affiliated (Modern start ~36%) **[VERIFY/TWEAK]**.
- **Chapter houses** (off-campus/on-campus land) — player can lease land for a new house.
- Chapter stats: Size, GPA, Philanthropy $, Service hours, Risk score.
- Effects: Belonging ++, Social ++, alumni donations ++ (Greek alumni give more), Risk (hazing/alcohol incidents).
- **Recruitment week** event in January **[VERIFY timing]**.
- Player actions: Chapter suspension, GPA minimums, deferred recruitment policy, risk management training, new chapter expansion.

### 16.3 Sensitivity
Hazing is a real, serious issue; treat incidents as consequential (suspension choices, safety investment), never as punchlines.

---

## 17. Athletics

### 17.1 RedHawks
- Colors: **Red & White**. Mascot: **Swoop** (a red hawk). **[see §36 trademark note]**
- Conference: **Mid-American Conference (MAC)**; hockey in **NCHC** **[VERIFY]**.
- **Cradle of Coaches**: Miami's legacy of alumni/coaches who became legends (Paul Brown, Weeb Ewbank, Ara Parseghian, Bo Schembechler, and others **[VERIFY list]**). In-game: "Coaching Tree" — each head coach you hire has a chance to become a Legend who later earns Heritage when they succeed elsewhere.

### 17.2 Sports simulated
Football, Men's/Women's Basketball, Hockey, Volleyball, Baseball, Softball, Soccer, Swimming, Track, Field Hockey, Synchronized Skating (Miami is a powerhouse **[VERIFY]**), plus club sports abstracted.

### 17.3 Mechanics
- Each team: Coach (Recruiting, Tactics, Development), Roster Strength, Facilities score, Budget.
- Season sim with weekly results in news ticker; big games get a **Game Day** event (crowds, traffic, tailgates, noise).
- **Rivalries:**
  - **Battle of the Bricks** vs. Ohio University (and the wider MAC "brick" rivalry)
  - **Victory Bell** vs. Cincinnati — one of the oldest rivalries in college football **[VERIFY]**
- Postseason: MAC Championship, bowl games, NCAA tournaments, Frozen Four.
- Revenue: tickets, conference/TV distributions, sponsorships, merch; typically subsidized by the university (mid-major reality).
- **Big decision:** new basketball arena to replace aging arena (Millett-style) — ~$240M project, huge event venue upgrade **[VERIFY]**.

---

## 18. Town–Gown (City of Oxford & Uptown)

### 18.1 Uptown Oxford
- High Street district with bars, restaurants, cafés, shops, bookstores. **Owned by the town**, simulated as a business ecosystem.
- Business health depends on student foot traffic, spending power, events, and seasons (summer slump!).
- **Business names [LOCKED: real]:** Uptown uses **real business names**, the places Miamians remember.
  - **Always positive portrayal:** no negative event (incident, fire, closure, health violation, underage service) is ever attributed to a named real business. Those events use a generic "an Uptown bar/restaurant" label.
  - **Names, not logos:** storefronts show the name in a simple generic sign style; real logos only if an owner gives permission.
  - **Courtesy outreach:** let owners know and honor any request to be removed (names live in `/data/uptown.json` so they're easy to change).
  - **Era-accurate:** businesses appear and disappear by their real opening/closing years where known; legendary gone-but-not-forgotten spots are a big nostalgia opportunity (a "Remember when?" codex entry when one closes) **[VERIFY dates]**.
  - Pre-1900 Uptown uses period-appropriate invented names where real records are thin.
- Iconic nights: Thursday–Saturday Uptown crowds (visual: sidewalk crowds, string lights).

### 18.2 Town Relations score inputs
| + | – |
|---|---|
| Economic impact (student spending, jobs; Miami is the county's largest employer **[VERIFY]**) | Noise, especially off-campus parties |
| Shared events (Red Brick Friday-style town celebrations **[VERIFY]**) | Parking overflow into neighborhoods |
| Payments/partnerships (fire/EMS, infrastructure) | Trash, vandalism |
| Community programs, volunteering | Buying town land |
| Transit service shared with residents | Tax-exempt land expansion |
| | Rental housing pressure / rent prices |

### 18.3 Off-campus housing market
- Mile-Square rentals: supply grows slowly; rent rises if demand > supply.
- Upperclass students choose on- vs. off-campus by price & quality.
- Landlord events: slumlord scandal, new luxury complex opens (competes with your halls).

### 18.4 City Council
Periodic votes: noise ordinance, zoning, parking rules, joint projects. Low Town Relations → your projects face delays.

---

## 19. Traditions & Heritage

### 19.1 Tradition system
Traditions are unlockable/emergent "living rules" that add **Heritage** per year while active, plus small quirky events.

| Tradition | How it forms | Effect |
|---|---|---|
| **Don't Step on the Seal** | Build the Seal | Heritage +; comedic events; students path around it |
| **The Miami Merger** | Upham Arch landmark | Alumni couples; donation multiplier |
| **Slant Walk** | Pave the big desire path | Heritage +; walk speed + |
| **Love & Honor** | Convocation + alma mater played at events | Belonging + (alma mater melody: original composition in-game, see §29) |
| **Formal Gardens photos** | Gardens landmark | Admissions conversion + |
| **Homecoming Parade** | Unlock Homecoming | Alumni return |
| **Winter snowball fight on the quad** | Big snowfall + large quad | Fun + |
| **Hockey student section chants** | Ice arena + winning seasons | Athletics hype |
| **Green Beer Day** (spring) | Emerges on its own once Uptown is established; see §23.5a | Heritage +, festive Happiness from the atmosphere & alcohol-free festival; safety risk managed by the player |
| **Freedom Summer remembrance** | Memorial landmark | Civic engagement +, Heritage + |

### 19.2 Heritage decay
Demolishing historic buildings, ending traditions by policy, or rebranding carelessly → Heritage ↓, alumni protest mail event.

### 19.3 History Book (in-game codex)
Unlockable entries about real Miami history (founding 1809, closing & reopening, McGuffey Readers, Western College, Freedom Summer, name change, etc.) — written carefully and sourced **[VERIFY each entry]**.

---

## 20. The Myaamia Relationship

**This needs to be handled with genuine respect and, ideally, consultation with the Myaamia Center / Miami Tribe of Oklahoma before shipping.** **[ACTION: reach out for review]**

### Real context (for the codex, not a gameplay gimmick)
- The university is named for the **Myaamia (Miami) people**, whose homelands include this region. The university maintains a formal, reciprocal relationship with the **Miami Tribe of Oklahoma**, including the **Myaamia Center** (language & cultural revitalization research) and a Myaamia classroom on campus.
- In **1996–1997**, at the Tribe's request, the university changed its athletic name to **RedHawks** **[VERIFY dates]**.

### Game treatment
- **Not** a resource to exploit and **no** stereotyped imagery, ever.
- Represented as a **Partnership track** (non-transactional): funding the Myaamia Center, supporting Myaamia students (real heritage award/scholarship program **[VERIFY]**), language revitalization research, respectful naming/signage.
- Benefits framed as **institutional integrity** (Heritage, Belonging, Academic Quality in relevant departments), not "points for representation."
- Campaign Chapter 6 is about the relationship and a respectful rebrand, written from the institution's perspective with Tribe-approved text only.
- **[LOCKED] Partnership track** is the chosen approach. It has no fail state and no "grind"; it grows through sustained, long-term commitments (annual support, not one-time purchases), mirroring how real relationships work.
- **Consultation plan:** Contact the Myaamia Center early and ask them to review the partnership track, the codex text, and any Myaamia-language terms used in-game. If review isn't possible before a public release, the track ships with **factual, university-published language only**, and nothing is invented.

---

## 21. Sustainability & Utilities

### 21.1 Energy
- **Heating:** Coal steam (early eras) → Natural gas steam → **Geothermal / simultaneous heating-and-cooling** conversion (real Miami direction) → electric heat pumps.
- **Electricity:** Grid (carbon intensity varies by era) + on-site **solar fields** (Western Campus-style) + rooftop solar.
- **Cooling:** Chillers; A/C in halls (big demand).
- Each building: energy use (kWh/yr), fuel type, efficiency rating (upgrades: insulation, LED, smart controls, LEED certification).

### 21.2 Carbon goal
- Track **MTCO₂e/year**. Goal: **Carbon neutral by 2040** (real stated goal **[VERIFY]**).
- Geothermal well fields: drill hundreds of wells under quads (construction disrupts lawns temporarily!).
- Sustainability grants, student approval, ranking badge.

### 21.3 Water, waste
- Water: water towers and pumping use the same coverage-radius model as other utilities (§21.4).
- Stormwater: paved area ↑ → flooding events in heavy rain; rain gardens & permeable paths fix.
- Waste: landfill cost vs. recycling/compost investment.

### 21.4 Utilities network **[LOCKED: coverage radius]**
- Each utility building (steam/power plant, geothermal field, chiller, water tower, solar field) has a **service radius** and a **capacity**.
- Buildings inside a radius are served, up to capacity; overlap is fine. Uncovered or over-capacity buildings show a warning icon and lose function (no heat in winter → closure; no cooling → happiness ↓).
- The overlay shows radii and load. Upgrades extend radius or capacity.
- Radii scale with era (1820s wood stoves = per-building; later central steam plants cover whole districts).

---

## 22. Transportation & Parking

- Students mostly walk; bikes/scooters optional.
- **Buses**: campus shuttle routes (player draws routes & stops), plus a regional bus system connection **[VERIFY BCRTA]**. Late-night routes improve Safety.
- **Transit Hub** (Chestnut Street Station–style): regional connections, rideshare, bike storage.
- **Parking**: Faculty/staff, commuters, visitors, residents' cars. Lots are ugly & space-hungry; garages are expensive. Permit pricing = revenue vs. happiness.
- Game-day traffic jams on key roads (US-27, High St, Patterson Ave-style roads **[VERIFY]**).
- Move-in day traffic mini-game: stagger schedules, add volunteers ("Move-in crew").

---

## 23. Events, Crises & Opportunities

Pop-up decision cards with 2–4 choices; each shows projected effects (can be hidden on Hard).

### 23.1 Weather / Nature
- **Blizzard** (cancel classes? cost of plowing vs. learning loss)
- **Ice storm** (injuries, power outages)
- **Polar vortex** (steam plant strain)
- **Tornado warning** (shelter drills; rare damage)
- **Heat wave at move-in** (A/C-less halls suffer)
- **Flooding** (low-lying fields)
- **Emerald ash borer** (lose trees unless treated)
- **Spring bloom** (beauty bonus)

### 23.2 Health
- Flu season, norovirus in a dining hall, **pandemic** (remote learning toggle; revenue crash; long scenario).

### 23.3 Financial
- State budget cut / increase
- Stock market crash (endowment –20%) / boom
- **Mega-gift** from alumni ($50M–$250M; naming rights demands)
- Federal research funding freeze
- Credit downgrade
- Enrollment shortfall

### 23.4 Academic
- Star professor poached
- Faculty union drive (neutral framing; bargaining outcomes)
- Accreditation visit
- Nobel/major award for faculty → Reputation spike
- Plagiarism / research misconduct scandal
- State mandate to cut low-enrollment programs
- New hot major demand surge

### 23.5 Student life
- Student protest (issue randomized & neutral: tuition, sustainability, dining, housing) → negotiate / ignore / compromise
- Viral TikTok of campus (Beauty-based) → applications +
- Greek hazing incident
- Dining hall food poisoning
- Housing overflow (lounges converted to triples)
- **Green Beer Day** — see §23.5a. **[LOCKED: included]**
- Uptown fire / building damage

### 23.5a Green Beer Day & alcohol-related events — design rules **[LOCKED]**
Green Beer Day is a beloved, well-known Miami tradition, so it's in the game as a big spring event with a festive Uptown atmosphere (green everywhere, crowds, music, packed High Street). It is designed so the game **never promotes or rewards underage drinking**.

**Hard rules for all alcohol content in the game:**
1. **Age is simulated.** Every student has a real age. Only agents **21+** are ever shown drinking or entering bars as drinkers.
2. **No reward for underage drinking.** Underage drinking can occur in the sim (it's realistic), but it only ever produces **negative** outcomes: citations, health incidents, conduct cases, parent complaints, Town Relations –, Safety –. It never raises Fun, Happiness, or any score.
3. **The player is rewarded for a safe day, not a wild one.** The event's score ("Green Beer Day Report" from the VP Student Life) rates safety: incidents, hospital transports, arrests, property damage, class attendance, and Town Relations.
4. **Fun comes from the tradition, not the drinking.** Happiness bonuses come from the atmosphere, green-themed campus events, free food, music, and alcohol-free alternatives that under-21 students (and everyone else) can enjoy.
5. **No drinking games, no "drink" mini-games, no achievements for consumption.** Achievements are for safety outcomes (e.g., *"Safe & Green — zero hospital transports"*).
6. **Ratings awareness:** content stays in a "Teen"-equivalent range: alcohol referenced, not glamorized.

**Player levers on the day:**
| Lever | Effect |
|---|---|
| Extra police/EMS staffing (with the City of Oxford) | Incidents ↓, cost ↑, Town Relations + |
| Campus-wide alcohol-free festival (food trucks, green pancakes, concert on the quad) | Under-21 students choose it → underage incidents ↓, Happiness + |
| Free breakfast in dining halls | Health +, incidents ↓ |
| Education campaign in the weeks prior | Incidents ↓ (small, cumulative over years) |
| Medical amnesty policy | Students call for help sooner → serious incidents ↓ |
| Late-night safe-ride buses | Safety + |
| Keep classes running / move exams off that day | Attendance vs. stress trade-off |
| Work with Uptown bars on ID checks | Underage incidents ↓, Town Relations + |

**The same rules apply to** Uptown nightlife, tailgates, Homecoming, St. Patrick's Day, and Greek events.

### 23.6 Athletics
- Upset win over a Power conference team → hype
- Coach scandal
- Conference realignment offer
- Frozen Four / March Madness run

### 23.7 Town
- City council parking ordinance
- New apartment complex approved
- Uptown business closures in summer
- Joint festival

### 23.8 Fun / flavor
- Student steps on the Seal before finals (chaos)
- Couple married after meeting under Upham arch 30 years ago returns with a donation
- Goose attack at the lake
- Squirrel stole a student's bagel
- Wi-Fi outage in King Library during finals (riot risk)
- Campus cat becomes unofficial mascot

---

## 24. Policies & Decisions

Toggle/slider policies with ongoing effects & costs.

| Category | Policies |
|---|---|
| Admissions | Test-optional, Early Decision, Legacy preference, Holistic review weight, Transfer pathway, Indiana tuition reciprocity |
| Finance | Tuition Promise cohort pricing, Merit vs. need aid split, Tuition cap compliance, Endowment payout rate (3.5–6%), Hiring freeze |
| Housing | 2-year residency requirement, Gender-inclusive housing, Themed LLCs, A/C mandate |
| Student life | Alcohol amnesty, Deferred Greek recruitment, Student activity fee level, Late-night dining, Mental health days |
| Academic | Class size caps, Online/hybrid courses, Winter Term, Required study abroad credit, General education overhaul (Miami Plan–style) |
| Campus | Georgian style mandate, Car-free core, Tobacco-free campus, Tree canopy target |
| Sustainability | Meatless Mondays, Green building standard, Coal phase-out date |
| Athletics | Athletics fee, Scholarship count, Facility priority |
| Town | Payment in lieu of taxes, Joint police patrols, Off-campus conduct code |
| Labor | Staff wage floor, Adjunct pay, Faculty raise pool % |

---

## 25. Progression, Unlocks & Research Tree

### 25.1 Era unlocks (Campaign/Historic sandbox)
Buildings, policies, and tech unlock by era: 1820s → 1880s → 1900s → 1950s → 1970s → 2000s → 2020s → Future.

### 25.2 Strategic Plan tree (Modern era & later campaign chapters)
Spend **Strategic Points** (earned yearly from Trustee Confidence & Reputation) on branches:

- **Academic Excellence (Teacher-Scholar):** Honors expansion → Faculty development center → Undergraduate research program → Interdisciplinary research centers → Endowed teacher-scholar chairs → Distinguished professors
- **Student Success:** Advising → Early alert system → LLCs → Guaranteed internships → 4-year grad guarantee
- **Access & Affordability:** First-gen programs → Need-based aid → Regional pathways → Free tuition for low-income Ohioans
- **Campus Beauty & Heritage:** Georgian code → Gardens → Historic restoration → National Register designations
- **Sustainability:** LED → Geothermal pilot → Solar fields → Full geothermal → Carbon neutral
- **Innovation & Tech:** Data science → AI programs → Makerspaces → Startup incubator
- **Athletics:** Performance center → New arena → Conference upgrade
- **Global:** Luxembourg expansion → International recruiting → Global partnerships
- **Community:** Town partnerships → Shared transit → Uptown revitalization fund

---

## 26. Regional Campuses & Global Presence

Shown as a **Region Map** tab managed through panels (budget, programs, enrollment, transfer pipeline), not built block-by-block **[LOCKED for v1]**. Buildable regional campuses may come later (Stretch).

| Location | Role | Mechanics |
|---|---|---|
| **Hamilton** campus | Commuter, applied/associate degrees, workforce | Adult learner enrollment, pathways to Oxford, workforce grants (advanced manufacturing hub **[VERIFY]**) |
| **Middletown** campus | Commuter, access | Same, different local demographics |
| **West Chester (Voice of America Learning Center)** | Professional/graduate programs | Grad revenue |
| **Luxembourg (Dolibois European Center)** | Study abroad | Semester abroad slots (~300/yr); Global score; happiness |

Regional enrollment helps weather the enrollment cliff; transfer pipeline to Oxford.

---

## 27. UI / UX

### 27.1 Main HUD
- **Top bar:** Date/semester, speed controls, Cash, Enrollment, Happiness, Reputation rank, Trustee Confidence.
- **Left:** Build menu (categories: Academic, Housing, Dining, Student Life, Athletics, Admin/Utilities, Landscape, Landmarks).
- **Right:** Demand bars; notification feed.
- **Bottom:** News ticker ("The Miami Weekly"–style parody paper headlines).
- **Overlays button** (hotkey O).

### 27.2 Panels / Screens
- **Budget** (revenue/expense table + sliders + 10-yr projection chart)
- **Admissions** (funnel: applicants → admits → deposits → enrolled; maps of origin)
- **Academics** (college → department tree, faculty list, program demand)
- **Students** (search/filter; click any student for profile & story log)
- **Rankings** (history chart; breakdown)
- **Athletics** (teams, schedules, results)
- **Town** (Uptown health, relations, council agenda)
- **Heritage** (traditions, landmarks, codex)
- **Sustainability** (carbon chart vs. 2040 target)
- **Trustees** (yearly goals, confidence meter, meeting events)
- **Policies**
- **Strategic Plan tree**

### 27.3 Inspect tools
Click building → stats, occupancy, condition, upgrades, renovate/demolish. Click student → profile, needs radar, schedule, story. Click path → foot traffic count.

### 27.4 Notifications
Priority tiers: Critical (red), Advisory (yellow), Flavor (grey). Advisors pop in with portrait + one-liner.

### 27.5 Controls
- Mouse/keyboard (3D camera): pan (WASD / middle-drag), zoom (wheel), orbit (Q/E or right-drag), tilt (R/F), place (click), cancel (Esc).
- Hotkeys: Space pause, 1–4 speed (1×/2×/4×/8×), 5 skip to next event (§6.1), B build, O overlays, F follow student, T time skip.
- Touch: pinch zoom, two-finger pan, long-press inspect.

---

## 28. Art Direction

### 28.1 Visual style **[LOCKED: low-poly 3D]**
- **Stylized low-poly 3D**: clean faceted geometry, flat or gently gradient-shaded materials, minimal textures (a subtle brick pattern on walls is the main exception).
- Real-time lighting with a sun that moves by time of day and season; soft shadows; ambient occlusion.
- Day/night cycle (lamplit brick paths at night are a must); warm window glow in halls at night.
- Seasonal changes: trees swap foliage meshes/colors (fall oranges, bare winter branches), snow accumulates on roofs and lawns via shader, spring blossoms in the Formal Gardens.
- Weather: rain, snow particles, fog in the creek valleys at dawn.
- Camera "postcard mode" for screenshots (depth of field, tilt-shift option).

### 28.1a Asset pipeline
- Modeling in **Blender**; export glTF 2.0 (`.glb`) into Godot.
- **Modular Georgian kit**: wall segments (brick), corner pieces, multi-pane windows with white trim, doors with fanlights, porticos & columns, slate roof pieces (hip, gable), dormers, **cupolas** (several sizes), clock faces, chimneys, cornices. Kit grid: **3 m bays** (one window each) and **3.5 m storeys** **[TWEAK]**.
- **Procedural kit assembler:** buildings are assembled from the kit **by code**, not by hand. It places wall bays along each footprint edge (stretching or filling the remainder), corners at the vertices, and a hip roof on top. It is driven by a short per-building **recipe** in data (storeys, bay rhythm, portico, cupola, era). The assembler builds both the real historic buildings (from their footprints) and the player's buildings. Phase 1 handles rectangles and L-shapes; arbitrary real footprints come with the modern campus (Phase 2).
- **Window panes and muntins are drawn by a shader** on the glass, not modelled, so a hall with ~90 window bays fits the triangle budget. Pane counts (6-over-6, 9-over-9 …) are shader parameters.
- **Hero landmarks** (Upham arch, the Seal, King Library, Sesquicentennial Chapel, Kumler Chapel, Yager, Goggin, Formal Gardens, Slant Walk) are custom-modeled.
- **Era variants**: the same building can look construction-stage, new, weathered, or renovated. New and weathered are **shader settings** (tint, grime); construction-stage uses scaffolding props and a height-clip shader. Separate renovated models are made only where a renovation really changed the shape.
- Naming, pivots, budgets, LODs, materials and automatic checks: `docs/ART_PIPELINE.md`; numbers in `/data/art_pipeline.json`.
- Target budgets: typical building 1–5k tris; hero landmark ≤15k; person figure ≤500 tris with 3–4 LODs; aggressive LOD + instancing.
- Characters: simple low-poly figures with color variations (skin, hair, clothing: hoodies, winter coats, game-day red), a few shared animations (walk, idle, sit, carry backpack), era-appropriate clothing sets (1820s → 2020s).

### 28.2 Palette
- **Miami Red** (approx. `#C3142D`) **[VERIFY official hex from Miami's brand guidelines]**
- Brick reds: `#8E3B2E`, `#A94A3A`, `#B85C47`
- White trim: `#F4F1EA`
- Slate roofs: `#4A5563`
- Copper/verdigris cupolas: `#5E9C8A`
- Lawn greens: `#6DA34D`, `#4E8A3A`
- Limestone: `#D9CFBF`

### 28.3 Architectural kit (Georgian Revival)
Red brick walls, white columns/porticos, multi-pane windows with white frames, slate hip roofs, **cupolas & clock towers**, dormers, fanlights, brick paths with herringbone pattern, black iron lamp posts, white benches.

### 28.4 Characters
Low-poly figures with variety (skin tones, hair, clothing: hoodies, backpacks, winter coats, game-day red). Faculty with tweed & coffee. Tour guides walking backwards (a must).

### 28.5 UI style
Paper/parchment panels with red accents, serif headings (e.g., a Garamond-like), clean sans body. Icons flat line-art.

---

## 29. Audio

- **Music:** Original score — acoustic guitar, piano, light strings; seasonal tracks; game-day brass band tracks (original compositions evoking a college marching band).
- **Alma mater & fight song** **[VERIFY exact titles]**: Since the game uses real Miami branding, include these **only with permission** (ask as part of the licensing request in §36), and use newly recorded performances rather than existing recordings unless those are licensed too. Until then, use an original "campus anthem" placeholder in the same spirit. **[VERIFY rights status]**
- **Ambience:** Birds, wind in trees, distant chatter, bike bells, snow crunch, rain, crowd roar from stadium, bells on the hour, Uptown bustle at night.
- **UI sounds:** Paper rustle, soft chimes, brick "thunk" on placement.
- **Voice:** Short advisor barks (text + optional gibberish voice à la Animal Crossing).

---

## 30. Technical Architecture **[LOCKED: Godot 4, desktop first]**

### 30.1 Stack
- **Engine:** **Godot 4.7.2 .NET build**, with the C# projects targeting **.NET 10** (Godot's C# API targets .NET 8, whose support ends Nov 2026).
- **Languages:**
  - **C#** for the simulation core (agents, economy, academics, admissions, pathfinding), for performance at 30k+ agents.
  - **GDScript** for UI, scene glue, tools, and quick iteration.
  - **GDExtension (C++)** only if profiling shows C# isn't enough for the hottest loops (pathfinding / movement).
- **Renderer:** Forward+ (desktop). Keep shaders compatible with the Mobile renderer for the future iPad port.
- **Version control:** Git + Git LFS for `.blend`, `.glb`, textures, audio.
- **Target platforms:** Windows, macOS, Linux (v1). iPad later; UI designed with touch-sized targets in mind from the start.
- **Min spec target:** 4-core CPU, 8 GB RAM, GTX 1060 / Apple M1-class GPU at 1080p / 60 fps **[TWEAK]**.

### 30.2 Full-population simulation (every student & faculty member)
The whole population is simulated every tick; only a subset is drawn.

- **Data-oriented design:** agents are **not** Godot nodes. They live in C# **structure-of-arrays** (e.g., `float[] needSleep`, `int[] currentBuilding`, `ushort[] majorId`) for cache-friendly iteration.
- **Simulation thread:** the sim runs on worker threads, decoupled from rendering; the main thread reads a snapshot for drawing.
- **Tick model:** 1 sim tick = 1 in-game hour. Sub-systems run at different rates:

  | System | Rate |
  |---|---|
  | Location / schedule (where each agent is) | every tick |
  | Needs & happiness | every tick |
  | Academics (learning, grades), research progress | daily |
  | Health, relationships, clubs | daily |
  | Enrollment decisions (stay / transfer / stop out) | per semester |
  | Admissions, budget, rankings | yearly |

- **Two-level movement:**
  - *Logical:* every agent is "in building X" or "walking from A to B". Each walk is settled **within the hourly tick**: the sim computes the departure and arrival minutes from the route length. Cheap, and runs for everyone.
  - *Visual:* the rendered walkers are a **sample** of the current hour's real walks, drawn within a detail radius of the camera and moving at a **visual-only walking speed**. At 1× an in-game hour lasts 83 ms of real time, so true walking speed can't be shown.
- **Pathfinding:** **flow fields** computed per destination building (shared by thousands of students) and recomputed only when paths or buildings change. Rebuilds run on a background thread and are swapped in when ready, so the game never freezes. Route lengths feed the walk-time / late-to-class system. Desire paths form from **recent, sustained** foot traffic on grass tiles, with regrowth (§12.4).
- **Rendering subset:** ~1,500–3,000 agents drawn, chosen by camera frustum, zoom, and priority (followed students, event participants). Drawn with **MultiMeshInstance3D** + GPU vertex-animation textures, so thousands of figures cost a few draw calls. At far zoom, crowds become simple impostors.
- **Determinism:** seeded RNG per system for reproducible saves and bug reports.
- **Performance budget:** a full sim tick at 30k students + 2.5k faculty ≤ **8 ms** on min-spec CPU at 1×, measured as the **95th percentile over 1,000 ticks** (average and max reported too). Higher speeds batch several ticks per frame **[TWEAK]**.
- **Early technical spike (done in Phase 0):** 30k agents + flow fields + MultiMesh rendering. Result: **p95 3.5–4.0 ms**, measured on 4 slow efficiency cores as the min-spec stand-in (`docs/PHASE0_REPORT.md`). Re-check on the real map and on a real 4-core machine.

### 30.3 World & map
- **Terrain:** heightmap from USGS elevation data, converted to chunked low-poly meshes by a **custom mesh generator** (decided after Spike B; Terrain3D was trialled, see `docs/TERRAIN_COMPARISON.md`). **400 × 400 m chunks (40 × 40 tiles) with 3 distance LODs**; one flat-shaded quad per tile. Changed land rebuilds only the affected chunks.
- **Tile data layer:** a 2D grid (~400 × 400) storing land state, ownership, path type, foot traffic, utility coverage, heritage value, snow depth.
- **Era system:** a timeline resource lists every historic building (footprint, real site, build/demolish dates, model variants) plus town-growth rules; the map for any year is derived from it + player actions.
- **Buildings:** Godot scenes instanced from data. Interiors are simulated but not rendered (click → info panel).
- **Nature:** trees via MultiMesh with seasonal variants; foliage shader for fall color and snow.

### 30.4 Data-driven content
All balance and content lives in data files (JSON or Godot `.tres` resources), so this doc maps directly to data:
```
/data/buildings/        one file per building (stats, era, style, model refs)
/data/landmarks/
/data/eras.json         unlock tables, prices, road types, clothing sets by era
/data/timeline.json     real historic building dates & sites (with sources, confidence, verified flags)
/data/schedules.json    daily routines, meals, class slots, free-time activities
/data/rendering.json    presentation settings: colours (palette refs), crowd, terrain, seasons, sun, camera
/data/art_pipeline.json art rules: kit grid, triangle budgets, LOD distances, special materials
/data/map/              map pipeline config, land-state rules, land-history rules, generated layers
/data/spikes/           test setups for technical spikes (not game content)
/data/departments.json
/data/research.json     fields, clusters, grant types
/data/policies.json
/data/events/           one file per event card
/data/traditions.json
/data/branding.json     logos, names, colors (swappable if licensing changes)
/data/balance.json      every [TWEAK] value
/data/codex/            history entries (with sources)
```

### 30.5 Map import pipeline (offline tools)
1. Download USGS 3DEP 1 m lidar DEM tiles → reproject to the campus-centred transverse-Mercator grid → crop to the map extent → resample to 5 m → export a 16-bit heightmap.
2. Pull OpenStreetMap footprints, land use, water, roads and paths (Overpass API) → rasterize to the tile grid, and keep the vector outlines for building footprints.
3. Hand-trace historic maps (Sanborn, archival plans) in QGIS → export per-era layers.
4. A Python script / Godot editor plugin bakes everything into Godot resources. (Steps 1–2 and 4 are built: `tools/map_pipeline/`.)

### 30.6 Project structure
```
love-and-honor/
  addons/                 editor plugins (map importer, data validators)
  assets/models/          .glb from Blender
  assets/audio/
  data/                   see 30.4
  scenes/                 world, UI, menus
  scripts/sim/   (C#)     population, economy, academics, research, admissions, pathing
  scripts/ui/    (GDScript)
  tools/                  offline Python pipeline
  tests/                  sim unit tests (C#) + performance benchmarks
```

### 30.7 Testing
- Unit tests for sim formulas (C#).
- Headless benchmark scene run on every change to catch performance regressions.
- "Autoplay" soak test: run 50 in-game years unattended and check for runaway values.

---

## 31. Save System
- Autosave every semester; unlimited manual slots; quicksave hotkey.
- Stored in Godot's `user://saves/`; binary-compressed (population arrays are large) with a JSON header for metadata and a thumbnail.
- Versioned format with migration code.
- Export/import saves to share with friends; screenshot and time-lapse export (GIF/MP4) for sharing.
- Cloud saves (Steam Cloud / iCloud): post-v1.

---

## 32. Balancing Formulas (starting points) **[TWEAK all]**

**Student Happiness**
`H = Σ(need_i × weight_i) / Σ weight_i`, weights: Sleep 1.2, Food 1.0, Study 0.9, Social 1.1, Fun 0.8, Health 1.0, Money 0.9, Belonging 1.3, Safety 1.0, Commute 0.6. Trait modifiers ±30%.

**Retention probability (year 1→2)**
`R = 0.70 + 0.20×(H/100) + 0.08×(GPA/4) + 0.05×(Belonging/100) – 0.10×(FinancialStress/100)` clamped 0.5–0.98.

**Applications**
`Apps = BasePool(demographics) × RepFactor^1.4 × MarketingFactor × PriceElasticity × BeautyVisitFactor`

**Yield**
Per admit logistic: `P = σ(a×Rep – b×NetPrice/Income + c×Visit + d×AidOffer – e×CompetitorPull)`

**Building condition**
Loses 1–3%/yr (by type); maintenance budget restores; < 40% → breakdown events; < 20% → closure.

**Maintenance cost**
`Upkeep = BaseUpkeep × (1 + Age/50) × (1.5 if condition < 50)`

**Reputation drift**
Moves 10% per year toward the "true" quality score (lagging perception).

**Alumni giving**
`Gift = Wealth × Loyalty × HeritageMult × (1.5 if Greek) × (2 if Miami Merger) × CampaignBoost`

**Difficulty presets**
| | Easy | Normal | Hard | "Provost" (Brutal) |
|---|---|---|---|---|
| Start cash | ×2 | ×1 | ×0.6 | ×0.4 |
| Event frequency | ×0.6 | ×1 | ×1.3 | ×1.6 |
| State funding | ×1.2 | ×1 | ×0.85 | ×0.7 |
| Enrollment cliff | Off | On | Steep | Very steep |
| Effect previews on cards | Shown | Shown | Hidden | Hidden |

---

## 33. Accessibility
- Colorblind-safe overlays (patterns + color); don't rely on red/green only (tricky with Miami red!).
- Scalable UI text (100–200%).
- Full keyboard controls; remappable keys.
- Reduced motion option; photosensitivity-safe fireworks.
- Subtitles for all audio barks.
- Pause-anytime; no timed-only decisions (Easy/Normal).

---

## 34. Tutorial & Onboarding
1. **"First Day on the Job"**: Welcome letter from the Board. Place a path, a classroom, a residence hall.
2. **Move-In Day**: watch the first class arrive; meet your first student (named, followed).
3. **Needs & demand bars**: build dining, library.
4. **First budget** and tuition setting.
5. **First event card** (a snow day).
6. **Commencement**: first graduates become alumni → first donation.
Tooltips everywhere; "Ask the Provost" help button explains any metric.

---

## 35. Achievements (examples)
- **Public Ivy** — Reach Top 50.
- **Cradle of Coaches** — Produce 3 Legendary coaches.
- **Mother of Fraternities** — Found all Triad chapters.
- **Don't Step on It** — 1,000 students avoid the Seal in one semester.
- **Miami Merger** — 100 alumni couples.
- **Slant Walk** — Designate the Slant Walk.
- **Oxford Bubble** — Town Relations 90+ for 10 years.
- **Snow Day? Never.** — Survive a blizzard without canceling classes and without injuries.
- **Carbon Zero** — Reach neutrality.
- **All Brick Everything** — 100% Georgian style harmony in the core.
- **Frozen Four** — Win a hockey national championship.
- **Victory Bell** — Win 5 straight rivalry games.
- **Endowment Club** — $2B endowment.
- **No Deferred Maintenance** — Backlog $0.
- **Tour Guide** — Draw a tour route that converts 60%+ of visitors.

---

## 36. Legal, Trademark & Sensitivity Notes

### 36.1 Using real Miami branding **[LOCKED]**
The game uses **real Miami University names, logos, marks, building names, and colors**. These are the university's trademarks, so:
- **Request permission** from Miami University's trademark/licensing office (and University Communications & Marketing) for a **non-commercial fan / portfolio project**. Describe it as free, non-commercial, respectful, shared with friends and the Miami community, and shown in a portfolio. **[ACTION]**
- Ask specifically about: the primary logo and wordmarks, the university seal, RedHawks athletic marks and Swoop, building names, use of campus photography as reference, and the alma mater and fight song (§29).
- **Build so branding is swappable:** all names and logos come from `/data/branding.json` and a branding asset folder. If permission is limited or denied, a stand-in set drops in without touching game code.
- Show a clear **"Unofficial fan project — not affiliated with or endorsed by Miami University"** notice unless the university says otherwise.
- Don't sell the game or accept donations while using the marks without a license.

### 36.2 Other real-world content
- **Real buildings:** depicting real architecture is fine; donor-named building names are included in the branding request.
- **Competitor schools:** real names only, no logos, neutral portrayal (§14.2).
- **Real people:** no current administrators, faculty, coaches, or students as characters. Historical figures appear only in factual codex text.
- **Uptown businesses:** real names used, always portrayed positively, logos only with permission, removed on request (§18.1).
- **Student newspaper:** fictional name unless *The Miami Student* agrees.
- **Map data:** OpenStreetMap requires attribution (ODbL); USGS data is public domain; check each archive's terms for historic maps.
- **Music:** original compositions unless licensed.

### 36.3 Sensitivity
- **Myaamia content:** consultation plan in §20.
- **Alcohol:** hard rules in §23.5a; the game never promotes or rewards underage drinking.
- **Hazing, mental health:** treated seriously with realistic consequences; resource info (e.g., 988 Lifeline) in info panels for mental-health events.
- **Politics:** protest and state-policy events use neutral framing; no sides on real partisan issues.
- **History:** the codex covers hard parts of history honestly (e.g., the name change, Freedom Summer), with sources.

---

## 37. Scope & Roadmap

### Phase 0 — Foundations (2–3 weeks) — **done** (`docs/PHASE0_REPORT.md`)
- Godot 4 .NET project, repo, Git LFS, folder structure, data schema. ✔
- **Spike A — Population:** 30k students + 2.5k faculty on schedules, flow-field pathing, MultiMesh rendering of a subset. Must meet the §30.2 budget. ✔ Passed (p95 3.5–4.0 ms).
- **Spike B — Map:** real Oxford heightmap in low-poly, build-grid overlay, land-state layer, timeline slider 1809 → 2026 showing footprints appear. ✔
- **Art:** done as **pipeline prep** (Blender → glTF → Godot workflow, rules, automatic checks, one placeholder kit piece; `docs/ART_PIPELINE.md`). The modular Georgian kit and the first buildings (a generic hall, Elliott) **moved to Phase 1**; Upham and King Library follow as hero landmarks.
- Licensing and Myaamia Center outreach emails: **drafted** (`docs/outreach/`); sending is up to the author.

### Phase 1 — Vertical slice: Campaign Chapter 1, "The Hill" (4–6 weeks; realistically ~6–9)
Checkpoints and proposed cuts: `docs/PHASE0_REPORT.md` §5 (1a–1k).
- Map as of 1824 (derived from the 1809 start state); land clearing and acquisition; first buildings via the kit assembler; paths and desire paths.
- Calendar, budget, tiny enrollment (dozens → ~250 students), needs and happiness, a handful of faculty (teaching + early scholarship).
- 10–15 era-appropriate events; codex entries; save/load; time-lapse.

### Phase 2 — Modern sandbox (2026) slice (6–8 weeks)
- Pre-placed real modern campus; full-population sim at scale.
- Admissions funnel, tuition and aid, State Share of Instruction, rankings.
- Academics including the **teacher-scholar research system** and undergraduate research.
- Uptown and town–gown basics; Green Beer Day under the §23.5a rules.

### Phase 3 — v1.0
- Remaining campaign chapters, Greek life, athletics sim, sustainability, policies, strategic plan tree, regional campuses panel, all landmarks and traditions, achievements, full audio, polish, accessibility, desktop builds for Windows/macOS/Linux.

### Stretch
- iPad port, fully buildable regional campuses, Endless legacy mode (after base game), mod support, photo-mode extras, narrated codex.

---

## 38. Open Questions

### Resolved
Everything in the **Locked Decisions** table at the top (items 1–21).
- **Terrain approach** (v0.4): **custom mesh generator**, decided after Spike B (§30.3, `docs/TERRAIN_COMPARISON.md`).

### Still open
1. **Anything Miami-specific** to add (favorite spots, traditions, Uptown places, inside jokes)? Open any time.

---
*End of v0.4. Next step: Phase 1, checkpoint 1a (the simulation on the real map), per `docs/PHASE0_REPORT.md`.*
