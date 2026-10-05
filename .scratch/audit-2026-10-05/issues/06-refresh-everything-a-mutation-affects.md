GitHub: #243
Status: ready-for-agent
Blocked by: none

# Refresh everything a mutation affects

## Parent

#237

## What to build

The dashboard stays stale after a monobank sync (`useMonobank.ts:25-28` skips the periods key) and after an invoice is issued, cancelled or deleted (`useInvoices.ts:60-64`). A client rename does not refresh invoices. Settings forms copy server data into state once, so a form open across a restore shows and then saves the old values.

## Acceptance criteria

- [ ] Sync, invoice and client mutations invalidate the dashboard and the lists that show them.
- [ ] Settings forms reset from server data when it changes after a restore.
- [ ] Tests for each invalidation.

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-05-full-audit.md, Web M3, M4, M5.
