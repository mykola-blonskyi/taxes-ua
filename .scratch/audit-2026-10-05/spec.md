GitHub: #237

## Problem Statement

The full audit of 2026-10-05 (`reports/audits/2026-10-05-full-audit.md`) found no Critical issue, but several ways the app can fail or show the owner a wrong picture:

- **The dashboard crashes** with a 500 when the year's group 3 income is negative, which a January refund of a December receipt produces.
- **A foreign-currency bank receipt can be lost for good** when the NBU rate lookup fails and the sync moves past it.
- **Two write paths skip the owner lock**, so concurrent refunds can exceed the receipt and a payment can be recorded twice.
- **The web can lose or misstate data**: a failed background refetch wipes unsaved edits; a sign-out leaves the previous account's data in the cache; the dashboard stays stale after a sync or an invoice change; a settings form open across a restore writes the old values back; network failures show no error; three saved settings do nothing.
- **Engineering gaps**: the boundary test misses constants read from shared code, tests run Postgres 16 against production's 18, pre-migration dumps are not encrypted, the weekly restore check trusts any newer file, and docs drifted.

## Solution

- The dashboard never fails on valid ledger states, and the limit bar compares in kopecks.
- Every write path that checks ledger invariants holds the owner lock.
- A bank window with a failed rate is re-read until it records.
- Web forms keep their data on refetch errors, the cache is per-session, mutations refresh everything they affect, and every failure is shown.
- The boundary test covers shared code, tests run on the production Postgres, backups and dumps are encrypted and authenticated, docs match the code.

## User Stories

1. As the owner, I want the dashboard to open after I refund last year's receipt, so that I am not locked out in January.
2. As the owner, I want the limit warning to fire at the line, not 500 UAH early.
3. As the owner, I want every foreign-currency receipt recorded even when the NBU was briefly down, so that income is never understated.
4. As the owner, I want a double-tapped refund or payment recorded once.
5. As the owner, I want my unsaved edits to survive a network blip.
6. As the owner, I want no data from a previous session after signing out.
7. As the owner, I want the dashboard current after a sync or an invoice change.
8. As the owner, I want a clear error when saving fails for any reason.
9. As the owner, I want settings that do something, or no setting.
10. As the owner, I want the deploy dumps and backups protected like the nightly backup.
11. As a maintainer, I want the boundary test and the test database to match reality.
