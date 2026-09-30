# Draft 1 — Miami University trademark & licensing request (§36.1)

**Status: DRAFT — not sent.** Fill in every `[BRACKET]` and check every `[VERIFY]` before sending.

- **To:** `[VERIFY: Miami University trademark / licensing office — current contact from miamioh.edu]`
- **Cc:** `[VERIFY: University Communications & Marketing contact]` (§36.1 names both offices)
- **Subject:** Permission request — non-commercial fan game set at Miami University ("Love & Honor")
- **Attach:** 3–4 screenshots (suggested: `docs/images/timeline-2026.png`, `timeline-1900.png`,
  `terrain-custom-campus.png`, `art-import-kit-piece.png`). Screenshots of real Miami marks don't exist yet, which is fine.

---

Dear [NAME / Trademark & Licensing team],

My name is [YOUR NAME], [YOUR CONNECTION TO MIAMI, e.g. "a Miami alum (Class of 20XX)" / "a current student"].
I'm building **Love & Honor**, a free, non-commercial fan game set at Miami University, and I'd like to ask
permission to use some of the university's names and marks in it.

**The project.** It's a low-poly 3D city-builder: the player guides Miami from its 1809 charter to the present
day on a map of the real Oxford terrain, placing buildings, running the academic calendar and keeping students
happy. It's a personal project for friends, the Miami community and my portfolio. It will be free, with no
sales, ads or donations. It's for desktop computers, maybe iPad later.

**What I'd like to use:**
1. The Miami University name, primary logo and wordmarks
2. The university seal
3. RedHawks athletic marks and Swoop
4. Building names, including donor-named buildings
5. Campus photography as modelling reference (for the 3D buildings, not shown in the game)
6. The alma mater and fight song. If these aren't available I'll use original music.
7. The title itself: "Love & Honor" is a phrase closely associated with Miami, so I want to check it's okay to
   use as the game's name.

**How I'll handle them:**
- An "Unofficial fan project — not affiliated with or endorsed by Miami University" notice, unless you'd prefer
  different wording.
- No selling, ads or donations while the game uses the marks.
- Respectful portrayal. For example, the game never promotes or rewards underage drinking, and serious topics
  like mental health are handled carefully.
- Everything is swappable. All names, colours and logos live in one settings file, so I can change or remove
  anything quickly, and I'll follow any conditions you set.
- I'm happy to share builds for review before anything goes public.

If full permission isn't possible, partial permission would still help (for example, names but not logos). So
would any brand guidelines you can share; I'd like to get the official colours right. And if the answer is no,
I'd appreciate knowing that too, and I'll use a fictional stand-in.

Thank you for your time. Screenshots of the current prototype are attached, and I'm happy to answer questions
or send more.

Best regards,
[YOUR NAME]
[EMAIL] · [PHONE, optional] · [PORTFOLIO / PROJECT LINK, optional]

---

## Notes for you (not part of the email)

- **Why each ask is there:** item list = §36.1's "ask specifically about" list, plus the title (my addition — the
  phrase "Love and Honor" is strongly tied to Miami, so it's the one thing that can't be swapped by data alone).
- **Facts to check before sending:** your connection to Miami; whether the game will ever be public (the email says
  "before anything goes public"); the platform line.
- **The "official colours" line** doubles as the `[VERIFY official hex]` in §28.2 (Miami Red is `#C3142D` in
  `branding.json` for now, unverified).
- **If they say yes with conditions:** record them in `data/branding.json` → `permission_status` (`granted` /
  `limited`) and in the Phase 0 decisions log; any required notice text goes in `branding.json` → `disclaimer`.
- **If they say no:** switch to `data/branding/standin.json` (already built, same schema). The title may need to change —
  that would be a Locked Decision change, so it comes back to you.
- Keep a copy of the sent email and any reply (see `README.md` in this folder).
