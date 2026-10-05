GitHub: #249
Status: ready-for-agent
Blocked by: none

# Tidy backend Lows from the 2026-10-05 audit

## Parent

#237

## What to build

Small items, each with file:line in the report: monobank `account.ExternalId` logged at Warning (`MonobankStatementImport.cs:146,162-164,181-182`); a deploy that stops mid-send cancels that reminder for good (`ReminderSender.cs:234`); `shadcn` listed as a production dependency; no connection-pool limit or lock timeout.

## Acceptance criteria

- [ ] Logs carry `account.Id`, not the bank id.
- [ ] A reminder send uses the channel's own timeout, not the shutdown token.
- [ ] `shadcn` moves to devDependencies; `pnpm audit --prod` is clean.
- [ ] Pool size and lock timeout are set in the connection string.

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-05-full-audit.md, Security N3, N6; Architecture F7.
