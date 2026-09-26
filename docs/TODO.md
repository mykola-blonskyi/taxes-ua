# TODO

Detailed plan in [plans/current.md](../plans/current.md), future work in
[plans/backlog.md](../plans/backlog.md).

## Backlog

- [ ] Stage 2. Bank import, reminders.
- [ ] Stage 3. Invoices, XML declarations, archiving.

---

## Planned

- [ ] MVP tickets #4–#18, then #20 (VPS deploy, last).

---

## In Progress

- [ ] #2 Set up CI.

---

## Review

- [ ] #3 Google sign-in and interface shell.

---

## Done

- [x] Spec read, questions resolved, plan approved (2026-09-25).
- [x] Repository scaffold: .NET solution, Next.js app, Docker, Central Package Management, web
      layer boundaries (PR #19).
- [x] MVP spec and 17 tickets published to GitHub Issues (#1–#18).
- [x] Agent skills configured: GitHub tracker with local mirror, triage labels, domain docs.

## Open questions for the owner

- [ ] EP/VZ payment deadline: counted from the declaration's statutory date, shifted off a
      weekend.
- [ ] ESV in the registration month: full amount or prorated.
- [ ] Is there official employment with an employer paying ESV.
- [ ] Replace the shadcn neutral palette in `web/src/app/globals.css` with the prototype's colour
      tokens. The prototype is an Obsidian note outside the repository, so #3 shipped the default.
- [ ] Does removing an address from `Auth__AllowedEmails` have to end a live session, or is
      deleting the user row the answer? ADR-005 is silent and #3 made the allowlist an entry gate.
      No session can be revoked server-side today, which [ADR-009](decisions.md) explains, so
      answering yes costs a ticket store.
