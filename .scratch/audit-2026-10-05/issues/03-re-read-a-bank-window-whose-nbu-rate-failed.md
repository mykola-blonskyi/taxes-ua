GitHub: #240
Status: ready-for-agent
Blocked by: none

# Re-read a bank window whose NBU rate failed

## Parent

#237

## What to build

`MonobankStatementImport.RecordAsync` (`:336-342`) skips a foreign-currency receipt when the NBU rate lookup fails and the sync position still advances (`:298-306`). Only the 31-day re-read (`:73`) retries it, so a failure during a backfill or after a sync gap over 31 days loses the receipt for good and understates income.

## Acceptance criteria

- [ ] The sync position does not move past a window with a rate failure; the next run re-reads it.
- [ ] Duplicate protection by bank operation id keeps the re-read idempotent (test).
- [ ] A test with a stubbed NBU failure then recovery records the receipt once.

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-05-full-audit.md, Backend #2.
