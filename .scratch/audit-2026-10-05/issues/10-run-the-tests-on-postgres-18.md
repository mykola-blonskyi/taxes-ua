GitHub: #247
Status: closed
Blocked by: none

# Run the tests on Postgres 18

## Parent

#237

## What to build

Production runs Postgres 18; `ApiFixture.cs:43`, `ci.yml`, `docker-compose.local.yml` and the README use 16.

## Acceptance criteria

- [ ] All four use 18 and CI is green.

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-05-full-audit.md, Architecture F6.
