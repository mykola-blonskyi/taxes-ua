# Current Plan

## Goal

Stage 1, MVP: track receipts, compute EP/VZ/ESV, show deadlines, keep a payment ledger with
balances, a home screen, declaration numbers, settings, export and backup, PWA. Backend on .NET,
frontend on Next.js, deployed to Coolify.

Implementation runs as agent lanes, one ticket slice per branch and worktree, verified
independently before merge. Tickets are approved and
published to GitHub Issues in `mykola-blonskyi/taxes-ua`: spec
[#1](https://github.com/mykola-blonskyi/taxes-ua/issues/1), tickets #2–#18 and #20, index in
[tickets.md](tickets.md), mirror in `.scratch/mvp/`. Below is each ticket unpacked into steps:
files, types, verification commands. Order follows the dependency graph (native GitHub
dependencies). Run `snippets/frontier.sh` for the live frontier instead of reading the order here.

## Progress

- **#2 set up CI. Done**, closed. `.github/workflows/ci.yml` on `main`, green.
- **#7 deadline calendar, engine half. Done.** `DeadlineCalendar` and its two input records are on
  `main` (PR #23, hardened by PR #24). The API endpoint and the web table remain, blocked on #4, so
  the issue stays open.
- **#8 accruals, engine half. Done.** `IncomeLedger`, `Accruals`, the `TransactionInput` closed union
  and `EngineWarning` are on `main` (PR #28). 99 engine tests, zero mismatches against an independent
  3,273-scenario model of Rules 1 and 3.
- **#3 sign-in and shell. Merged, issue still open.** PR #29 landed auth, the shell, theme and uk/ru,
  plus four security fixes found by independent verification. It stays open for one criterion: five of
  six screens cannot be measured at 375px while the auth gate is client-side only.
- **#9 balances, engine half. Done.** `Balances` and `ObligationBuilder` on `main` (PR #30). Rule 7's
  "kinds are never mixed" is a property of the type: `YearBalances` names one field per kind and holds
  no collection over them, so there is nothing to iterate or sum across.
- **#3 sign-in and shell. Closed.** PR #29 then PR #32. Four security findings fixed, including a
  fail-open where `IsProduction()` is not the complement of `IsDevelopment()`, so an empty or
  misspelled environment name skipped the `ALLOWED_HOSTS` pin entirely.
- **#4 year parameters and FOP settings. Closed.** PR #37. `EsvDeadlineDay` and
  `AdvanceRecommendedDay` are bounded 1..28, the bound independent verification of the engine asked
  for. 2026 seeds to `EsvMonthlyKop` 190,234 and `IncomeLimitKop` 1,009,104,900.
- **#16 passkey. Merged, open for device checks.** PR #36. `SignInManager.PasskeySignInAsync` is
  deliberately unused: it signs a user in without consulting the allowlist.
- **#18 PWA. Merged, open for device checks.** PR #35. The Lighthouse criterion named an audit that
  Lighthouse 13 no longer has; installability is now checked through Chrome's own engine.
- **#33 blank-screen fix. Closed.** PR #34. Every non-401 failure of `/api/auth/me` rendered an empty
  page with a clean console.
- **Verification is now a committed tool.** `.claude/skills/verify-taxes-ua/` plus
  `scripts/measure-screens.mjs` and `scripts/verify-passkey.mjs`. Every UI ticket drives a real browser
  through the Development-only sign-in seam instead of asserting a screen works. Note its limit: it
  checks overflow, not usability. #4's first build measured clean while squeezing every number input
  in the tax-year table to a few pixels.
- **Test counts on `main`:** 165 engine, 106 api.
- Issue bodies now carry a Status block and checked-off criteria with their evidence. Findings that
  belong to a later ticket are filed as criteria on that ticket, not left in a comment thread.
- Three Rule 5 readings the 2026 reference table cannot settle are now written down in
  `knowledge/business-rules.md` (PR #26). The Q4 holiday-year question needs the owner's answer
  before #4 seeds holidays for a post-martial-law year.

## Two chains, not one

The ticket order reads as a single chain nine merges deep. It is not. The `TaxesUa.Engine` work is
pure functions over input records, with no database, no auth and no HTTP, so it runs concurrently
with the plumbing instead of behind it.

**Chain A, plumbing.** #3 auth and shell, then #4, #5, #6, then each engine ticket's API and web
half, then #13, #14, #15, #16, #17, #18.

**Chain B, engine.** #7 `DeadlineCalendar`, then #8 `IncomeLedger` and `Accruals`, then #9
`Balances` and `ObligationBuilder`, then #11 `LimitMonitor`. Each waits on its predecessor only
because they share the Engine's input records, never for an API or database reason.

A ticket closes when both its slices have landed. Splitting this way puts the tax arithmetic under
test early, which is where a wrong answer costs the owner a real penalty rather than a rerender.

**Backend layout follows ADR-008.** `TaxesUa.Api` organizes by feature, not by technical type:
`api/src/TaxesUa.Api/Features/<Name>/` holds that feature's EF entity, its DTOs, and a
`<Name>Endpoints.cs` static class exposing one `Map<Name>Api` extension method called from
`Program.cs`. `Data/AppDbContext.cs` only aggregates `DbSet<T>`; it owns no logic. Entities and
feature-internal types are `internal`, so the compiler — not eslint — enforces that features don't
reach into each other. `TaxesUa.Engine` stays a separate project (ADR-002), referenced by whichever
feature needs a computation, never duplicated.

**Deployment is deliberately last.** Every ticket up through #18 is built and verified against
the local Docker Compose stack (`docker compose -f docker-compose.yml -f docker-compose.local.yml
up --build`, UI at `http://localhost:3000`) or the local dev servers (`dotnet run --project
api/src/TaxesUa.Api` + `pnpm --dir web dev`). None of them need a live domain, Coolify, or the
VPS's Postgres. Only ticket #20, the very last one, goes to production.

Approved by the owner 2026-09-25. The detailed per-ticket plan approved 2026-09-25. Deployment
moved to the end of the sequence 2026-09-25. Backend layout switched to feature slices (ADR-008)
2026-09-25.

---

## Phase 1. MVP, by ticket

### #2. Set up CI — done, closed

`.github/workflows/ci.yml`: `dotnet test api`, `pnpm --dir web lint && pnpm --dir web build`,
`docker build ./api` and `./web`, on every push and PR. This needs no deployment target; it
exercises the same local build every ticket already verifies with.

Verify: the workflow is green on the branch; breaking `MoneyTests` locally and pushing makes it
fail; reverting makes it pass again.

Relies on: ADR-006, ADR-001.

---

### #3. Google sign-in and the interface shell — no blockers

`api/src/TaxesUa.Api/Data/AppDbContext.cs` → `IdentityDbContext<ApplicationUser>`,
`SchemaVersion = IdentitySchemaVersions.Version3`. `api/src/TaxesUa.Api/Features/Auth/
ApplicationUser.cs` (new, `internal`), `Features/Auth/AuthEndpoints.cs` exposing `MapAuthApi`:
`AddIdentityCore`/`AddGoogle` wiring, an allowlist check from `Auth:AllowedEmails`,
`/api/auth/login/google`, `/api/auth/callback`, `/api/auth/logout`, `/api/auth/me`. `Program.cs`
calls `app.MapAuthApi()`. Migration `AddIdentity`.

`web/src/i18n/` — next-intl config, `web/messages/uk.json`, `web/messages/ru.json`.
`web/src/shared/theme/` — `ThemeProvider` (`next-themes`), tokens from the prototype.
`web/src/shared/ui/` — shadcn/ui (`pnpm dlx shadcn@latest init`, alias `@/shared/ui`).
`web/src/app/layout.tsx` — wraps `ThemeProvider` + `NextIntlClientProvider`.
`web/src/app/(app)/layout.tsx` — navigation (mobile bottom bar / desktop sidebar), the disclaimer
(Rule 11). `web/src/data/auth/useMe.ts` — redirects to `/login` on 401.

Verify: signing in with an allowlisted email creates a session, `/api/auth/me` returns the user;
another email gets 403; the cookie is `HttpOnly; Secure; SameSite=Lax`; theme and language (uk/ru)
toggle and persist; every MVP placeholder screen opens at 375px with no horizontal scroll.

Relies on: ADR-005, ADR-008, Rule 11, domain-model `User`.

---

### #4. Year parameters and FOP settings — blocked by #3

`api/src/TaxesUa.Api/Features/Settings/Settings.cs`, `Features/Settings/SettingsEndpoints.cs`
(`GET/PUT /api/settings`). `api/src/TaxesUa.Api/Features/TaxYears/TaxYearConfig.cs`,
`Features/TaxYears/TaxYearEndpoints.cs`: `GET /api/tax-years`, `PUT /api/tax-years/{year}`,
`POST /api/tax-years/{year}/verify`, `POST /api/tax-years/{year}/clone-to/{next}`. `AppDbContext`
gains `DbSet<Settings>`, `DbSet<TaxYearConfig>`. Migration `AddTaxYearConfigAndSettings` + a 2026
seed (values from Rule 3/Rule 4) at startup. Recompute `EsvMonthlyKop` and `IncomeLimitKop` on
save via `Money.ApplyBp`/similar.

`web/src/features/settings/` — `index.ts`, `components/FopSettingsForm.tsx`,
`components/TaxYearTable.tsx`, `hooks/useSettings.ts`, `hooks/useTaxYears.ts`.
`web/src/data/settings/`, `web/src/data/tax-years/`. `web/src/app/(app)/settings/page.tsx`.

Verify: `GET /api/tax-years/2026` → `EsvMonthlyKop=190234`, `IncomeLimitKop=1009104900`; a test
for the derived-field recomputation; cloning 2026→2027 leaves `VerifiedAt=null`.

Relies on: ADR-007, ADR-008, Rule 3, Rule 4, Rule 9, domain-model `Settings`/`TaxYearConfig`.

---

### #5. Receipts in hryvnia — blocked by #4

`api/src/TaxesUa.Api/Features/Transactions/Client.cs`, `Features/Transactions/Transaction.cs`
(new: `AmountMinor: long`, `Currency`, `RateE4: int = 10000`, `AmountUahKop: long`, `Kind`,
`NonIncomeReason`), `Features/Transactions/TransactionsEndpoints.cs`:
`GET/POST/PUT/DELETE /api/transactions`, filter `?year=`, validation (amount > 0,
`NonIncomeReason` required when `Kind != Income`), UAH via `Money.ToUahKop` with `RateE4=10000`.
A warning when `ValueDate < Settings.FopRegistrationDate` (`warnings[]` in the response).
Migration `AddClientsAndTransactions`, index `(UserId, ValueDate)`.

`web/src/features/transactions/` — `index.ts`, `components/TransactionForm.tsx`,
`components/TransactionTable.tsx`, `hooks/useTransactions.ts`. `web/src/data/transactions/`.
`web/src/app/(app)/transactions/page.tsx`.

Verify: `dotnet test` — amount ≤ 0 and non-income without a reason validate as 400 (set up
`tests/TaxesUa.Api.Tests/Features/Transactions/` in this ticket if not already there); CRUD from
the screen; the year total equals Σ Income − Σ RefundToClient; an operation before registration
shows a warning.

Relies on: Rule 1, Rule 8, Rule 10, ADR-003, ADR-008, domain-model `Transaction`/`Client`.

---

### #7. Deadline calendar — engine half done, API and web half blocked by #4

`api/src/TaxesUa.Engine/DeadlineCalendar.cs` (new): `ForQuarter(int year, int quarter,
TaxYearConfigInput config, FopSettingsInput settings)` → statutory and shifted dates for ESV,
declaration, payment. Records `TaxYearConfigInput`, `FopSettingsInput` in the Engine.
`tests/TaxesUa.Engine.Tests/DeadlineCalendarTests.cs`: a table-driven test against the 2026
reference (Rule 5), tests for both values of `TaxPaymentCountsFromStatutoryDeclarationDate` and
`ShiftTaxPaymentFromWeekend`, and a test with a holiday.

`api/src/TaxesUa.Api/Features/Periods/PeriodsEndpoints.cs` (stub, completed in #8):
`GET /api/periods/{year}` returns the deadline dates.
`web/src/features/periods/components/DeadlinesTable.tsx` (partial).

Verify: `dotnet test --filter DeadlineCalendarTests` — all 12 dates of the 2026 reference match
(2026-04-20/2026-05-11/2026-05-20, 2026-07-20/2026-08-10/2026-08-19,
2026-10-19/2026-11-09/2026-11-19, 2027-01-19/2027-02-09/2027-02-19); Q4 yields 2027 dates.

Relies on: Rule 5 in full, ADR-002, ADR-008 (no `DateTime.Now`, only config as input).

---

### #6. Currency receipts and the NBU rate — blocked by #5

`api/src/TaxesUa.Api/Features/Fx/FxRate.cs` (new), key `(Currency, Date)`.
`Features/Fx/NbuRateClient.cs` (new): `HttpClient`, 5s timeout, falls back day by day up to 7
tries on an empty `[]`. `Features/Fx/FxEndpoints.cs`: `GET /api/fx?currency=USD&date=2026-09-26`.
Extend `Features/Transactions/TransactionsEndpoints.cs`: non-UAH requires `RateE4`,
`RateSource: Nbu | Manual`. `web/src/data/fx/useFxRate.ts` — auto-fills in
`TransactionForm.tsx` when date/currency changes.

Verify: an `NbuRateClient` test with a substituted `HttpMessageHandler` — an empty Saturday
response returns Friday's rate with `RateDate` set to Friday; a repeat request is served from the
`FxRate` cache, not the handler; NBU unavailability → 502 with a message, the form still allows a
manual entry. By hand: enter 100 USD dated on a Saturday, see Friday's rate with the date noted.

Relies on: Rule 2, ADR-003, ADR-008, domain-model `FxRate`.

---

### #8. Accruals and the declaration numbers — blocked by #6, #7

`api/src/TaxesUa.Engine/IncomeLedger.cs` (new): income by month/quarter/cumulative, excluding
operations before `FopRegistrationDate` with an `EngineWarning`. `Engine/Accruals.cs` (new):
`ForYear(...)` — EP/VZ cumulative minus already accrued (Rule 3), ESV over active months per
`EsvRegistrationMonthPolicy`/`EsvExempt`. Tests: registration mid-quarter (1 and 2 active
months), a refund in a different quarter, a refund larger than the month's income, `EsvExempt`,
the sum of quarterly EP equals cumulative EP for the year.

`api/src/TaxesUa.Api/Features/Periods/PeriodsEndpoints.cs`: `GET /api/periods/{year}` in full
(months, quarters, cumulative total, deadlines from #7), a stub warning about an unverified year
in the dashboard. `web/src/features/periods/` — `index.ts`, `components/QuartersTable.tsx`,
`components/DeclarationNumbers.tsx`, `hooks/usePeriods.ts`. `web/src/app/(app)/periods/page.tsx`.

Verify: engine scenario tests pass; the API response matches the engine's computation on the same
data; the tables match the prototype's column layout.

Relies on: Rule 1, Rule 3, Rule 8, ADR-002, ADR-008.

---

### #9. Budget payments and balances — blocked by #8

`api/src/TaxesUa.Api/Features/Payments/BudgetPayment.cs` (new: `Kind`, `AmountKop`,
`PeriodYear`, `PeriodQuarter?`, `PeriodMonth?`), `Features/Payments/PaymentsEndpoints.cs`: CRUD
`/api/payments`, period validation. `api/src/TaxesUa.Engine/Balances.cs` (new): accrued
cumulative minus paid per kind, overpayment carried forward within a kind, kinds never mixed.
`Engine/ObligationBuilder.cs` (new): assembles `Obligation[]` with statuses relative to `today`
(a parameter, not `DateTime.Now`). Tests: an ESV overpayment covers the next quarter; an EP
overpayment never offsets an ESV debt; statuses `Upcoming`/`Due`/`Overdue`/`Done`.

`web/src/features/payments/` — form, list, balances panel. `web/src/app/(app)/payments/page.tsx`;
"paid"/"remaining" columns in `QuartersTable`.

Verify: engine table-driven tests pass; CRUD through the API and the screen; balances show an
overpayment with the right sign.

Relies on: Rule 7, ADR-008, domain-model `BudgetPayment`/`Obligation`.

---

### #10. Home screen: the next step — blocked by #9

`api/src/TaxesUa.Engine/ObligationBuilder.cs` (extension) — `NextStep(today)`.
`api/src/TaxesUa.Api/Features/Dashboard/DashboardEndpoints.cs`: `GET /api/dashboard`, "today"
computed by `Europe/Kyiv` via `TimeZoneInfo` (not UTC). Tests: picking the next step with several
obligations/an overdue item/an empty list/a date before registration; the Kyiv-midnight vs UTC
boundary.

`web/src/features/dashboard/` — `components/HeroCard.tsx` (states: normal, overdue, clear,
before registration), `hooks/useDashboard.ts`. `web/src/app/(app)/page.tsx`.

Verify: a test for the Kyiv-midnight boundary; the hero matches the prototype; marking a payment
updates the step without a reload (TanStack Query invalidate).

Relies on: ADR-004, ADR-008, Rule 5.

---

### #11. Income limit — blocked by #10

`api/src/TaxesUa.Engine/LimitMonitor.cs` (new): percentage, 85/100 thresholds, excess, tax at
`ExcessRateBp`. Tests: 84.99%, 85%, 100%, excess by one kopeck. Add a `limitStatus` field to
`Features/Dashboard/DashboardEndpoints.cs`. `web/src/features/dashboard/components/LimitBar.tsx`.

Verify: tests at the thresholds; the bar changes color/text at 85% and 100%.

Relies on: Rule 4.

---

### #12. Monthly advances — blocked by #10

`api/src/TaxesUa.Engine/Accruals.cs` (extension) — monthly accruals. Tests: an advance larger
than the quarterly accrual → an overpayment; a partial advance → a remainder.
`web/src/features/periods/components/MonthsTable.tsx` (shown only when `PaymentMode ==
MonthlyAdvance`). `Features/Payments/PaymentsEndpoints.cs` and its forms gain a "month" period.

Verify: switching modes never changes accruals, only recommendations; the months table matches
the prototype.

Relies on: Rule 6.

---

### #13. CSV and XLSX export — blocked by #6

`api/src/TaxesUa.Api/Features/Export/ExportEndpoints.cs`:
`GET /api/export/transactions.csv|xlsx` (ClosedXML, add to `Directory.Packages.props`).
`web/src/features/transactions/components/ExportButtons.tsx`.

Verify: both files open cleanly in Excel/Numbers, Cyrillic survives (BOM for CSV), hryvnia
amounts with two decimals match the screen.

---

### #14. JSON backup and restore — blocked by #9

`api/src/TaxesUa.Api/Features/Backup/BackupEndpoints.cs`: `GET /api/backup`,
`POST /api/restore` (a database transaction, replacing the user's data).
`web/src/features/backup/` — buttons, a confirmation before replacing.

Verify: backup → wipe → restore yields identical data across every MVP entity; an invalid file →
400, nothing changes.

---

### #15. Prototype JSON import — blocked by #12

`api/src/TaxesUa.Api/Features/Backup/ImportEndpoints.cs`: `POST /api/import/prototype` —
`incomes → Transaction (RateSource: Manual)`, `mpaid → BudgetPayment × 3` (EP/VZ/ESV, "month"
period). Kept in `Features/Backup/` alongside `BackupEndpoints.cs` rather than as its own
feature — import is a backup-shaped operation, not a standing resource. Idempotence via tagging
imported records with a source external key.

Verify: re-importing the same file doesn't change the record count.

---

### #16. Passkey — blocked by #3

`api/src/TaxesUa.Api/Features/Auth/AuthEndpoints.cs` gains the passkey endpoints —
`Configure<IdentityPasskeyOptions>` (`ServerDomain`), `PasskeyCreationOptions`,
`PerformPasskeyAttestationAsync`, `PerformPasskeyAssertionAsync`. Passkey lives in the `Auth`
feature, not a separate one; it's a second sign-in method on the same resource.
`web/src/features/auth/components/PasskeyButton.tsx` — `navigator.credentials.create/get`.

Verify: passkey registration and sign-in on iOS Safari, Android Chrome, desktop; an invalid
attestation is rejected.

Relies on: ADR-005, ADR-008.

---

### #17. Change log — blocked by #9

`api/src/TaxesUa.Api/Features/Audit/AuditLog.cs`, `Features/Audit/
AuditSaveChangesInterceptor.cs` — before/after snapshots in jsonb for
Transaction/BudgetPayment/Settings/TaxYearConfig. Like `AppDbContext`, the interceptor
necessarily touches every audited feature's entity type; that's the one other place ADR-008's
per-feature isolation doesn't apply, and it's deliberate for the same reason. `Features/Audit/
AuditEndpoints.cs`: `GET /api/audit?entity&id`.
`web/src/features/audit/components/HistoryPanel.tsx`.

Verify: create → edit → delete a transaction yields three log entries with correct snapshots.

Relies on: ADR-008.

---

### #18. PWA and mobile polish — blocked by #3

`web/public/manifest.json`, icons, a Serwist service worker.

Verify: Lighthouse marks the app installable; it installs and runs standalone on iOS/Android; no
screen scrolls horizontally at 375px.

---

Tickets #13, #16, #17, #18 are off the critical path (#3 → #4 → #5 → #6 → #8 → #9 → #10 → #12
→ #15) and can be done whenever convenient after their blocker. The engine halves of #7, #8, #9
and #11 form Chain B above and run ahead of their nominal blockers.

---

### #20. Deploy to Coolify on the VPS — blocked by #4–#18 (every other MVP ticket)

The finished MVP goes live. Coolify: a Docker Compose resource from the repository (branch
`main` after merging [PR #19](https://github.com/mykola-blonskyi/taxes-ua/pull/19)), services
`web` and `api`, the domain → `web`, `api` with no external port. The database reuses the
PostgreSQL instance already running on the VPS: create a dedicated role and database for this
project instead of provisioning a new Coolify PostgreSQL resource, `DATABASE_URL` in env points
at it. If that instance has no backup already, add the project's own scheduled logical dump
(ADR-006).

Verify: `curl https://<domain>/api/health` → `{"status":"ok","database":true}`; `api` is not
reachable from outside; the new role can connect only to its own database; every MVP screen
(transactions, payments, dashboard, periods, settings, export, backup) works end to end against
the production deployment, matching what was already verified locally.

Relies on: ADR-006, ADR-001.

---

## Phase 2. Automation

See [backlog.md](backlog.md). Broken into tickets via `/to-tickets` once the MVP is closed.

---

## Phase 3. Documents

See [backlog.md](backlog.md). Broken into tickets via `/to-tickets` once the MVP is closed.

---

## Risks

- The EP/VZ payment-deadline interpretation, when the declaration date shifts, is not confirmed.
  Made configurable (`TaxPaymentCountsFromStatutoryDeclarationDate`,
  `ShiftTaxPaymentFromWeekend`).
- ESV in the registration month is not confirmed. Made configurable
  (`EsvRegistrationMonthPolicy`).
- Types between C# and TS drift when hand-duplicated. Types are generated from OpenAPI
  (`openapi-typescript`), never hand-written twice.
- The FOP is not registered yet. The registration date is in the future; real data arrives later,
  so the engine is verified against synthetic data and the 2026 reference table.
- The monobank API may not expose FOP accounts. Checked with the owner's own token in Stage 2.
