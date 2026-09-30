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

## Stage 2: monobank sync

Spec: issue #71. Mirror: `.scratch/stage-2/`. monobank only; PrivatBank is deferred.

| # | Ticket | Blocked by |
| --- | --- | --- |
| #74 | 01. Share transaction recording between the form and imports | none |
| #75 | 02. Connect monobank with a personal API token | none |
| #76 | 03. Sync the last 31 days of monobank FOP receipts | #74, #75 |
| #77 | 04. Backfill the year from monobank within the rate limit | #76 |
| #78 | 05. Review imported transactions with suggested kinds | #76 |
| #79 | 06. Pick up new monobank operations by webhook and nightly catch-up | #77 |
| #80 | 07. Turn Treasury payments into budget payment candidates | #78 |

Frontier at the start: #74 and #75.

## Stage 2: paying and the tax reserve

Spec: issue #97. Mirror: `.scratch/stage-2-pay/`. The app prepares payments (details, purpose, NBU QR) and never initiates them.

| # | Ticket | Blocked by |
| --- | --- | --- |
| #98 | 01. Remember the Treasury account for each tax kind | #80 |
| #99 | 02. Pay each obligation from a panel with ready payment details | #98 |
| #100 | 03. Add an NBU QR code to the payment panel | #99 |
| #101 | 04. Show how much to set aside for taxes | none |
| #102 | 05. Track a monobank jar as the tax reserve | #101 |

Frontier at the start: #101 (#98 waits for #80).

## Stage 3: invoices

Spec: issue #89. Mirror: `.scratch/stage-3-invoices/`. Bilingual EN/UK PDF, frozen at issue, paid by linked receipts.

| # | Ticket | Blocked by |
| --- | --- | --- |
| #90 | 01. Keep client details for invoices | none |
| #91 | 02. Enter my invoicing details and payment details per currency | none |
| #92 | 03. Draft, issue and download a bilingual PDF invoice | #90, #91 |
| #93 | 04. Mark invoices paid by linking receipts | #92 |
| #94 | 05. Suggest invoice payments for imported receipts | #93, #80 |

Frontier at the start: #90 and #91.

The rest of Stages 2 and 3 is not yet broken into tickets. Content in [backlog.md](backlog.md).
