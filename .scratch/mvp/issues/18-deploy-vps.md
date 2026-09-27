# 18: Deploy to Coolify on the VPS

GitHub: #20
Status: repository side closed in PR #62 (host filtering, security headers, persisted
data-protection keys, fail-fast startup, `deploy/postgres/` scripts, `docs/deploy.md`). Open for
the deploy itself: DNS, the Coolify resource, the Google client, the database role, and the first
release, all by hand per docs/deploy.md, followed by the #15/#16/#18 checks on the real domain.
Blocked by: #3, #4, #5, #6, #7, #8, #9, #10, #11, #12, #13, #14, #15, #17, #41, #47, #48, #49, #53
(#16 and #18 are verified against the production domain after this ticket, not before it)

## Parent

#1

## What to build

The finished MVP goes live on the owner's VPS. In Coolify: a Docker Compose resource is created
from the repository (services `web` and `api`), the domain is bound to `web`, `api` is not
published externally. Instead of a new Coolify PostgreSQL resource, a dedicated role and database
for this project are created in the PostgreSQL instance already running on the VPS
(ADR-006), and `DATABASE_URL` is set accordingly. If that instance has no backup already, the
project adds its own scheduled logical dump.

## Acceptance criteria

- [ ] `https://<domain>/api/health` returns `{"status":"ok","database":true}`, the home screen opens.
- [ ] The `api` service is not reachable from outside; the domain reaches only `web`.
- [ ] The new database role can connect only to its own database.
- [ ] Every MVP screen (transactions, payments, dashboard, periods, settings, export, backup) works end to end against the production deployment, matching its local behavior.
- [ ] If the existing PostgreSQL instance has no backup already, a scheduled logical dump is in place for this project's database.

## Blocked by

- #4, #5, #6, #7, #8, #9, #10, #11, #12, #13, #14, #15, #16, #17, #18, #19
