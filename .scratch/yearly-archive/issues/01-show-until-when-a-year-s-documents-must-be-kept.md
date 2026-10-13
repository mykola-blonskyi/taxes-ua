GitHub: #284
Status: ready-for-agent
Blocked by: none

# Show until when a year's documents must be kept

## Parent

#283

## What to build

The owner sees a keep-until date on each tax year and later in its archive. For each group 3 quarter of the year with a declaration, take the filing date if filed, else the statutory deadline, add 1095 days (Tax Code art. 44.3); the year's date is the latest. Martial-law suspension of limitation periods is a parameter (start, optional end), not code; while it has no end, the date is shown as extended while martial law lasts. Verify the current legal basis on zakon.rada.gov.ua before coding and cite it.

## Acceptance criteria

- [ ] An engine function computes the date: filed vs deadline, the latest quarter wins, suspension open and closed; engine tests cover each case.
- [ ] The suspension parameter is editable where year parameters are, seeded from the verified legal source.
- [ ] The API exposes the date per year; the periods/year view shows it in uk and ru, with the extension note while suspension is open.
- [ ] Business rules gain the rule next to Rule 14's retention paragraph, with sources.
- [ ] 375 px layout check passes.

## Blocked by

None (can start immediately)
