# TODO

Detailed plan in [plans/current.md](../plans/current.md), future work in
[plans/backlog.md](../plans/backlog.md).

## Backlog

- [ ] None.

---

## Planned

- [ ] Stage 3. The yearly document archive, spec #283: #284 keep-until date, #285 archive screen,
      #286 the year as one ZIP (after #285), #287 the reminder after Q4 is filed (after #284, #286).
      Owner decisions 2026-10-06: only documents the app makes; one reminder after Q4 is filed.

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

- [x] The 2026-10-05 audit (#237), done 2026-10-05: #238–#257, #260 and #261, including #245
      (display settings), #246 (boundary gap), #253 (prototype importer deleted), #254 (backup schema
      floor), #255 (`__Host-` cookie), #256 (hashed path secrets), #257 (admin-only tax years) and #251
      (this docs pass).

## Open questions for the owner

- [x] EP/VZ payment deadline shifted off a weekend or holiday: yes, Tax Code art. 57.1 (Rule 5,
      2026-10-06). Still open: whether the 10 days count from the statutory or the shifted filing date.
- [x] ESV in the registration month: the full minimum, by law (#171, Rule 3).
- [x] Is there official employment with an employer paying ESV: no (owner, 2026-10-06). An employed
      month would be exempt under ESV Law art. 4 part 6 only if the employer paid at least the minimum.
- [x] The prototype's colour palette: no prototype with colours exists, so the neutral palette stays
      (owner, 2026-10-06).
- [x] Does removing an address from `Auth__AllowedEmails` have to end a live session? Yes: the
      cookie's `OnValidatePrincipal` checks the ticket's email claim against the allowlist, with no
      ticket store (#289, ADR-005 and ADR-009 amendments of 2026-10-06).
