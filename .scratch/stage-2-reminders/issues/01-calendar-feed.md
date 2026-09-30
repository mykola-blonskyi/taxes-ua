# 01: Subscribe to a private calendar feed of deadlines

GitHub: #105
Status: ready-for-agent
Blocked by: none
Parent: #104

## What to build

A private calendar feed of all deadlines. Settings shows a subscribe URL at a per-owner secret path (256-bit, rotatable) serving an iCalendar document: one all-day event per deadline (ESV, single tax and military levy payment, declaration, and monthly advances in that mode) for the current and next year, with alarms 7 and 1 days before, kinds and periods but no amounts. The same document can be downloaded once as .ics. Dates are the engine's shifted dates.

## Acceptance criteria

- [ ] The feed parses back with an iCalendar parser and matches the engine's deadlines across a year with weekend and holiday shifts and monthly advances.
- [ ] No amounts in the feed; a wrong secret gets 404; rotating invalidates the old URL.
- [ ] Owner isolation; the secret is not in backups.
- [ ] Settings section proved in a real browser at 375 px in uk and ru; subscribed once from a phone calendar by the owner.
