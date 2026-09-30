# Draft 3 — Courtesy note to Uptown business owners (§18.1)

**Status: DRAFT — not sent.** Fill in every `[BRACKET]` before sending.

This is a courtesy note, not a permission request. §18.1 locks real business names in. The note lets owners know
they're in the game, and the game honours any request to be removed. A logo is the only thing that needs a yes.

**When to send:** once `/data/uptown.json` exists (it's planned, not built yet) and you know which businesses will
appear. One note per business. Keep a log of who was contacted and what they said (see `README.md`).

There are two versions: an email for businesses with a contact address, and a short message for social media or
in-person contact.

---

## Version A — email

**Subject:** A heads-up: [BUSINESS NAME] in a free fan game about Miami & Oxford

Hi [OWNER / MANAGER NAME or "there"],

I'm [YOUR NAME], [YOUR CONNECTION TO MIAMI / OXFORD]. I'm making **Love & Honor**, a free, non-commercial fan game
about Miami University and Oxford, from 1809 to today. Uptown is a big part of what people remember about Oxford,
so the game's Uptown uses the real names of the places people love, including **[BUSINESS NAME]**.

What that means for you:
- **Your name on a simple, generic storefront sign.** No logo unless you'd like one included, in which case just
  send it and say so.
- **Always positive.** The game never ties anything negative to a real business. Incidents, closures,
  inspections and the like are only ever shown as "an Uptown restaurant" or similar.
- **In the right years.** The shop appears in the game from the year it opened `[if known: YEAR]`.
- **Removed on request.** If you'd rather not be in the game, reply and I'll take your name out, no questions
  asked. You can ask at any time.

If you'd like to help with anything, I'd welcome: the year you opened (and, for older spots, any history you'd
like remembered), whether you'd like your logo used, and any corrections.

The game is free: no sales, ads or sponsorships. It isn't affiliated with Miami University or the City of Oxford.

Thanks, and thanks for being part of Uptown.

[YOUR NAME]
[EMAIL] · [PROJECT LINK, optional]

---

## Version B — short message (social media DM / in person)

> Hi! I'm making a free fan game about Miami & Oxford, and Uptown uses real business names, including
> [BUSINESS NAME]. It's always a positive portrayal and just your name on a simple sign (your logo only if you want
> it). If you'd rather not be included, just tell me and I'll remove it. Happy to share more. — [YOUR NAME]

---

## Notes for you (not part of the note)

- **Every promise in the note is already a rule:** positive-only portrayal, names not logos, removal on request, and
  era-accurate years (§18.1, §36.2). CLAUDE.md's content rules enforce the "no negative events on a named business"
  part.
- **Removal is a data change:** delete or disable the entry in `/data/uptown.json`. No code change, so it's fast.
- **Closed / historic businesses** (the "Remember when?" entries, §18.1): there may be nobody to contact. Use only
  published facts and mark dates `[VERIFY]`; if a former owner can be reached, the same note works with small
  wording changes.
- **Bars:** keep the note exactly as neutral as above. Don't mention Green Beer Day or drinking (§23.5a rules).
- **Facts to check before sending:** your connection to Miami/Oxford; the opening year, if you include it.
