# 03: Add the ESV annex to the annual declaration XML

GitHub: #112
Status: ready-for-agent
Blocked by: #111
Parent: #109

## What to build

For the annual (fourth-quarter) declaration, also produce the ESV annex (Додаток 1, F0133109) as a second XML file, linked from the declaration through LINKED_DOCS with the annex flag set, carrying the year's ESV for the FOP paying for themselves, and fill the declaration's ESV line from it. Vendored and validated like the declaration.

## Acceptance criteria

- [ ] Golden-file tests for a full year and a partial first year; both files validate; the declaration links the annex.
- [ ] The owner confirms the F0133109 XSD against the DPS register and imports the pair into the Cabinet without sending.
- [ ] Both files downloadable together from the Q4 Declaration screen.
