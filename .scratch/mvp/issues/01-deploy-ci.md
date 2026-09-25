# 01: Deploy the skeleton to Coolify and set up CI

GitHub: #2
Status: ready-for-agent
Blocked by: none

## Parent

#1

## What to build

The skeleton from the feat/scaffold branch runs on a real domain, and every following ticket can be verified against it. In Coolify: a Docker Compose resource is created from the repository. Instead of a new Coolify PostgreSQL resource, a dedicated role and database for this project are created in the PostgreSQL instance already running on the VPS. The domain is bound to the web service, DATABASE_URL points at the new role/database. GitHub Actions runs dotnet tests, web lint and build, and image builds on push and PR.

## Acceptance criteria

- [ ] `https://<domain>/api/health` returns `{"status":"ok","database":true}`, the home page opens.
- [ ] The `api` service is not reachable from outside.
- [ ] The new database role can connect only to its own database; if the existing Postgres instance has no backup already, the project has its own scheduled logical dump.
- [ ] The CI workflow is green on the branch and fails on a broken engine test.

## Blocked by

- None (can start immediately)
