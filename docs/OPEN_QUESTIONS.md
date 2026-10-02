# Open questions for Aidan

**Answered 2026-10-02.** Aidan answered the ones that change something; every other question keeps its default
(the defaults below are now decisions). Summary:

| # | Answer | What changed |
|---|---|---|
| Q20, Q25 | Keep the ~15-year pace | Nothing (placeholders stay) |
| Q30 | Deadline 1841 | Nothing |
| Q36 | Only a brick hall wins Chapter 1 | Goal counts Elliott, Stoddard or a new 1820s **Brick Residence Hall** (64 beds, $4,000); boarding houses still add beds |
| Q10 | Elliott/Stoddard look: close enough | Nothing (unverified) |
| Q31 | Research a few more events | 4 new sourced events: student self-government (Dec 1824), theological department and farmers' college (1829), 234 students and 7 professors (1834), Oxford Theological Seminary (1838); 2 new History Book entries |
| Q16 | The Slant Walk is the player's choice | Paving a long diagonal asks "Name this the Slant Walk?" (Yes / Not this one) |
| Q26 | Trustees: nothing more for now | Nothing |
| Q28 | Loans wait for Phase 2 | Nothing |
| Q21 | Add Saturday classes and chapel | 1820s day per Upham (1909): rise at 5, recitations at 7, 9, 10, 11 Mon–Sat, chapel at 6 a.m. and 8 p.m. in Old Main (Saturday classes unverified) |
| Q9 | Demolition waits for Phase 2 | Nothing |
| Q2 | Research the 1824 start | Found real fees (tuition $10 a session, room $5 a year: budget now $20/$5) and Old Main's 1820 contract (>$6,000: Heritage Project cost now $6,000); no acreage or rent figures online (they're in the Archives' ledgers), so the starting square and land rents stay placeholders |
| Q5, Q29 | Already settled | Q15 (path required) and the 1j ending screen |
| all others | Defaults kept | — |

The original questions follow for reference.

---

Collected while working through the Phase 1 checkpoints back-to-back (from 2026-10-02). **All of 1e–1k are done**
(see CLAUDE.md "Current phase" and the screenshots in `docs/images/`). Each question lists the
**default I went with** so work could continue; every default is easy to change. Answer in any order: a short
"yes / no / use X instead" per item is enough.

## Carried over from earlier checkpoints

**Q1 (Phase 0) — speed keys.** §27.5 says "1–5 speed", but there are five speed states including pause.
*Default:* Space = pause, 1–4 = 1×/2×/4×/8×, 5 reserved for "skip to next event" (§6.1).

**Q2 (1d) — Chapter 1 starting numbers.** The 1824 starting land (a 300 m square around Old Main), 20 students,
3 faculty and $3,000 are placeholders. Do you know (or want me to research) Miami's real 1824 land, enrollment and
faculty? *Default:* keep the placeholders, all marked `verified: false`.

**Q3 (1d) — 1824 academic calendar.** The modern calendar is applied to 1824 (Miami's early terms were different).
*Default:* keep it until the content research in 1j.

**Q4 (1d) — autumn colour.** The November woods render very orange. *Default:* leave it for the art pass.

## 1e — Placement

**Q5 — path at the door.** §12.1 says buildings need path access; players can't lay paths until 1g.
*Default:* a warning until 1g, then required (set `path_access.required` in `data/placement.json`).

**Q6 — 1820s construction times.** §12.1's 3 / 9 / 18–24 months are modern; an 1820s brick hall took longer.
*Default:* per-building times (wooden buildings 4 months, brick halls and Elliott 12 months). Should early eras be
slower overall (an era multiplier)?

**Q7 — when the modern generic halls unlock.** §11 says "Start" for the Small/Large Classroom Hall etc.
*Default:* "Start" = the modern (2026) start; in historic play they unlock in the early 1900s (`early20`).

**Q8 — Heritage Projects timing.** Real buildings are offered from 3 years before their real build year (Elliott
1825, Stoddard 1833). *Default:* 3 years. Earlier or later?

**Q9 — demolition.** Finished buildings can't be demolished yet (only construction can be cancelled). *Default:*
demolition comes with the Heritage score (Phase 2), since §12.3 ties it to a Heritage penalty. OK?

## 1f — Buildings v1

**Q10 — Elliott and Stoddard (you walk past them).** Their recipes are guesses: 4 storeys, gable roof, chimneys at the
ends, 6-over-6 windows, plain brick, no portico, the door in the middle of a long side. What's right? (Number of
storeys, roof shape, where the doors are, anything distinctive.) *Default:* the guess, marked `verified: false`.

**Q11 — Old Main (demolished 1958).** 3 storeys, hip roof, cupola, 12-over-12 windows: all guesses. *Default:* keep
until archival photos are checked (1j research).

**Q12 — roofs generated, not kit pieces.** The art plan listed roof kit pieces (hip, end, corners); the assembler
generates roofs instead (cheaper, fits any size, L-shapes for free). *Default:* generated roofs. OK?

**Q13 — big buildings and the 5k-triangle budget.** §28.1a says a typical building is 1–5k triangles. Big halls
(e.g. a 48 × 38 m L-shaped hall, 4 storeys) are 7k at full detail. *Default:* allow it for big buildings (≤ 35
triangles per window bay), with the middle-distance version inside 5k. OK, or should big halls get simpler windows?

**Q14 — only buildings with a recipe are drawn from the kit.** In the 2026 preview that's just Elliott and Stoddard; the
other ~80 real buildings stay plain blocks until arbitrary footprints are supported (Phase 2, per the approved plan).
*Default:* as planned.

## 1g — Paths

**Q15 — path at the door is now required** (this settles Q5 with its default). A new building needs a path next to its
entrance, so in 1824 the first job is a dirt path from the road. OK?

**Q16 — the Slant Walk is automatic.** §12.4 says the first major diagonal desire path "can be designated" the Slant
Walk. *Default:* the first paved desire path that's long (≥ 100 m) and diagonal (within 25°) becomes it automatically,
with a message. Should it be the player's choice (a button when paving)?

**Q17 — path surfaces.** New paths use the era's preferred surface: dirt (1820s), gravel (1880s), brick (1900s on;
concrete exists but brick wins). *Default:* no surface picker yet. Want one (e.g. cheap concrete vs brick, with the
Architect grumbling, §11.8)?

**Q18 — paths are instant and cheap** ($0.40 a tile in the 1820s; brick $40 a tile today). *Default:* placeholders;
no construction time for paths. OK?

**Q19 — removing the real campus footpaths.** On university land the player can remove any footpath, including the
real ones from the 2026 map, for free; roads can't be removed. *Default:* allowed. OK?

## 1h — People

**Q20 — 1820s enrollment numbers.** 24 applicants a year (growing 8% a year), 90% admitted, one faculty member per 12
students, and families in town who board about 230 students in 1825. With these, the 250-student goal takes roughly
15 years of building halls and classrooms. *Default:* placeholders until the 1j research; do you want Chapter 1 faster
or slower than that?

**Q21 — the 1820s school day.** Three recitations a day, Monday–Friday, at 8, 11 and 2. Early colleges also had
Saturday classes and daily chapel. *Default:* not modelled yet. Add them?

**Q22 — who gets the hall beds.** Hall beds go to first-years first, then second-years and so on (like the modern
"first- and second-years on campus" policy, §11.3); everyone else boards in town. *Default:* as described. OK for the
1820s, or should seniors get first pick?

**Q23 — faculty hiring is automatic.** Faculty are hired at move-in to keep one per 12 students (min 3). Real hiring
(§13.3: candidates, salaries, tenure) needs the budget. *Default:* automatic until 1i/Phase 2. OK?

**Q24 — breaks.** Students leave Oxford for the whole summer and winter break (they "go home" and don't walk around
campus). *Default:* as described.

## 1i — Economy

**Q25 — 1820s money.** Tuition $30 a year, room rent $10, township land rents $3,000 a year, a professor's salary $600,
administration $800, no state support. With these the first year runs a ~$1,200 surplus, so Elliott Hall ($5,000)
is affordable after two or three years. *Default:* placeholders until the 1j research (Miami's real finances relied
on its township land leases). Faster or slower pacing?

**Q26 — what the Trustees care about.** Once a year (Aug 1): +5 for a running surplus, −8 for a deficit, +3 if fall
enrollment grew, −6 if it fell; −3 every month cash is negative; dismissed at 0. *Default:* as described. Other
factors you want early (happiness, Heritage, the Chapter 1 goals)?

**Q27 — tuition.** The player sets tuition from 50% to 200% of the era's rate; it applies to all students from the
next term and changes applicants (20% higher tuition ≈ 16% fewer applicants). §8.4 describes cohort pricing (locked
for each entering class). *Default:* simple version now, cohort pricing later. OK?

**Q28 — paying for buildings.** Everything is paid in cash; bonds, donors and partnerships (§8.5) aren't in.
*Default:* add them in Phase 2. Want a simple loan earlier for Chapter 1?

**Q29 — being dismissed.** At Trustee Confidence 0 a banner appears and the game pauses. *Default:* the proper ending
screen comes with Chapter 1's win/fail conditions (1j).

## 1j — Chapter 1

**Q30 — the deadline.** Win: 250 students and a residence hall by August 1, 1841 (the end of President Bishop's term;
the real Old Miami reached 250 students in 1839). Lose: dismissed, bankrupt, or out of time. *Default:* as described.
Tighter or looser?

**Q31 — which events.** Eight historical events (literary societies 1825, McGuffey 1826 and 1836, The Literary Focus
1827, North Hall 1828, Oxford incorporated 1830, Alpha Delta Phi 1835, Beta Theta Pi 1839) and two invented ones (a
hard winter, a Trustees' visit), all in `data/events/`. Anything you'd add (e.g. something you know from campus lore)
or drop? The cuts allowed 10.

**Q32 — event cards pause the game.** *Default:* yes, until you press Continue. OK?

**Q33 — the Miami people and the university's name.** There's no History Book entry about the Myaamia people or how
the university got its name: §20 allows only university-published language until the Myaamia Center has reviewed it.
*Default:* leave it out until then. (The outreach draft in `docs/outreach/` is the place to ask.)

**Q34 — real historical people.** President Bishop and Professor McGuffey appear by name in the History Book, and
McGuffey as a faculty member hired by an event (historical figures, not current ones, so within the content rules).
OK?

**Q35 — after the chapter.** Winning shows a banner; "Keep playing" continues the same campus open-ended (Chapter 2
doesn't exist yet). *Default:* as described.

## 1k — Saves and time-lapse

**Q36 — what counts as "a residence hall" for Chapter 1.** Right now any residence building does, including a
$800 frame boarding house. §4.1 says "first residence hall (Elliott-style)". *Default:* any residence. Should only a
brick hall (Elliott, or a future brick-hall type) count?

**Q37 — autosaves.** One rolling autosave, overwritten at every move-in and spring term start, plus a quicksave and as
many manual slots as you like. *Default:* as described. Keep a few older autosaves too?

**Q38 — what the time-lapse shows.** Land (forest, cleared, town), ownership, roads and paths, and your buildings
rising and finished, one frame per month. Not walkers or desire paths. GIF/MP4 export was cut to Phase 3 (approved).
*Default:* as described.
