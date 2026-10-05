GitHub: #254
Status: ready-for-agent
Blocked by: none

# Restore only the current backup schema and stop bumping it for additive fields

## Parent

#237

## What to build

Owner decision 2026-10-05 (#252). The JSON backup schema is at v18 after 17 bumps in six days, each with an upgrade step and a fixture, most only filling null or `[]`. The nightly encrypted `pg_dump` (ADR-031) is now the disaster backup, so the JSON file no longer needs to read every past version. Set the support floor at v18: a restore of an older file is refused with a stable error code telling the owner to download a fresh backup. New optional fields get defaults so they need no version bump.

## Acceptance criteria

- [ ] Upgrade steps and fixtures below v18 are deleted; a v17 file is refused with a translated message.
- [ ] Adding an optional field with a default does not require a schema bump (a test proves an older v18 file without the field restores).
- [ ] ADR records the floor and the rule for when a bump is still needed.
- [ ] Release note in the PR: download a fresh JSON backup after this deploys.

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-05-full-audit.md, Architecture F4.
