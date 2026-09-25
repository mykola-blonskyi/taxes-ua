# Tickets

Source of truth: GitHub Issues in `mykola-blonskyi/taxes-ua`. Mirror: `.scratch/mvp/`.
Spec: issue #1 and [spec-mvp.md](spec-mvp.md). Tracker conventions: `docs/agents/issue-tracker.md`.

Tickets are vertical slices: each one cuts through schema, API, UI and tests, and can be verified
on its own. Blocking is set via native GitHub dependencies. A ticket can be picked up once all of
its blockers are closed.

Deployment to the VPS is deliberately last. Every ticket before it is built and verified
against the local Docker Compose stack (`docker compose -f docker-compose.yml -f
docker-compose.local.yml up --build`) or the local dev servers (`dotnet run` / `pnpm dev`), never
against a live domain. Only CI (GitHub Actions) runs early, since it needs no deployment.

## MVP (Stage 1)

| # | Ticket | Blocked by |
| --- | --- | --- |
| #2 | 01. Set up CI | none, **done** |
| #3 | 02. Google sign-in and the interface shell | none |
| #4 | 03. Year parameters and FOP settings | #3 |
| #5 | 04. Receipts in hryvnia | #4 |
| #6 | 05. Currency receipts and the NBU rate | #5 |
| #7 | 06. Deadline calendar | #4, **engine half done** |
| #8 | 07. Accruals and the declaration numbers | #6, #7 |
| #9 | 08. Budget payments and balances | #8 |
| #10 | 09. Home screen: the next step | #9 |
| #11 | 10. Income limit | #10 |
| #12 | 11. Monthly advances | #10 |
| #13 | 12. CSV and XLSX export | #6 |
| #14 | 13. JSON backup and restore | #9 |
| #15 | 14. Prototype JSON import | #12 |
| #16 | 15. Passkey | #3 |
| #17 | 16. Change log | #9 |
| #18 | 17. PWA and mobile polish | #3 |
| #20 | 18. Deploy to Coolify on the VPS | #4–#18 (every other MVP ticket) |

Critical path: #2 → #3 → #4 → #5 → #6 → #8 → #9 → #10, then #20 once everything else lands.
Frontier at the start: #2 and #3.

## Stages 2 and 3

Not yet broken into tickets. Content in [backlog.md](backlog.md). Broken down via `/to-tickets`
once the MVP is closed.
