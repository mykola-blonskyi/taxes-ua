GitHub: #251
Status: ready-for-agent
Blocked by: none

# Correct the docs drift found on 2026-10-05

## Parent

#237

## What to build

`docs/architecture.md` lists 4 audited types (the code audits 12) and an incomplete web feature list; `graph/architecture.md` still sizes `BackupDocument.cs` at 1,800 lines and lists #186 as pending; Postgres 16 is stated in docs.

## Acceptance criteria

- [ ] Each stale claim matches the code.

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-05-full-audit.md, Architecture F8.
