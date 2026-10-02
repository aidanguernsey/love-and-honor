# Open questions for Aidan

Collected while working through the Phase 1 checkpoints back-to-back (from 2026-10-02). Each question lists the
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
