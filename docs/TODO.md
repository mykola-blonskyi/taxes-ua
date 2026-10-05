# TODO

Detailed plan in [plans/current.md](../plans/current.md), future work in
[plans/backlog.md](../plans/backlog.md).

## Backlog

- [ ] Stage 3. The yearly document archive (invoices and the XML declaration are done, below).

---

## Planned

- [ ] The 2026-10-05 audit (#237): #238–#251 and #253–#258, owner decisions in #252. #238 first (dashboard 500).

---

## In Progress

---

## Review

---

## Done

- [x] Spec read, questions resolved, plan approved (2026-09-25).
- [x] Repository scaffold: .NET solution, Next.js app, Docker, Central Package Management, web
      layer boundaries (PR #19).
- [x] MVP spec and 17 tickets published to GitHub Issues (#1–#18).
- [x] Agent skills configured: GitHub tracker with local mirror, triage labels, domain docs.
- [x] MVP tickets #2–#18 and #20, the deploy to Coolify (closed by 2026-09-28).
- [x] Stage 2. monobank sync (#71), reminders in Telegram and email and the calendar feed (#104).
- [x] Stage 3. Bilingual PDF invoices (#89), declaration readiness and the F0103309 XML (#109).
- [x] The 2026-10-02 audit (#170): #171–#190. #185 added the API's feature boundary test and
      corrected these docs; #186 split the API files over 600 lines.

## Open questions for the owner

- [ ] EP/VZ payment deadline: counted from the declaration's statutory date, shifted off a
      weekend.
- [x] ESV in the registration month: the full minimum, by law (#171, Rule 3).
- [ ] Is there official employment with an employer paying ESV.
- [ ] Replace the shadcn neutral palette in `web/src/app/globals.css` with the prototype's colour
      tokens. The prototype is an Obsidian note outside the repository, so #3 shipped the default.
- [ ] Does removing an address from `Auth__AllowedEmails` have to end a live session, or is
      deleting the user row the answer? ADR-005 is silent and #3 made the allowlist an entry gate.
      No session can be revoked server-side today, which [ADR-009](decisions.md) explains, so
      answering yes costs a ticket store.
