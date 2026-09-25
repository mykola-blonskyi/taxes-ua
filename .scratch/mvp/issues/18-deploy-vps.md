# 18: Deploy to Coolify on the VPS

GitHub: #20
Status: ready-for-agent
Blocked by: #4, #5, #6, #7, #8, #9, #10, #11, #12, #13, #14, #15, #16, #17, #18, #19

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
