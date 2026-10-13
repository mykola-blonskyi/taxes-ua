GitHub: #285
Status: ready-for-agent
Blocked by: none

# Archive screen listing a year's documents

## Parent

#283

## What to build

One screen per tax year lists what the app already holds for it: issued and cancelled invoices (PDF from the frozen snapshot), declaration files and annexes per quarter with their filing marks, the receipts statement, and the budget payments register. Each item downloads on its own. Nothing new is stored.

## Acceptance criteria

- [ ] A read endpoint per year returns the list with a download link per item; missing quarters or no invoices read as empty sections, not errors.
- [ ] Every listed item downloads through an existing or new route, owner-scoped.
- [ ] The screen is reachable from the year's view and the navigation, in uk and ru, and passes the 375 px layout check.
- [ ] API tests through the fixture and a component test through the harness.

## Blocked by

None (can start immediately)
