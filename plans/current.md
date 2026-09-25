# Current Plan

## Goal

Stage 1, MVP: track receipts, compute EP/VZ/ESV, show deadlines, keep a payment ledger with
balances, a home screen, declaration numbers, settings, export and backup, PWA. Backend on .NET,
frontend on Next.js, deployed to Coolify.

Implementation is manual — the owner writes the code themselves. Tickets are approved and
published to GitHub Issues in `mykola-blonskyi/taxes-ua`: spec
[#1](https://github.com/mykola-blonskyi/taxes-ua/issues/1), tickets #2–#18, index in
[tickets.md](tickets.md), mirror in `.scratch/mvp/`. Below is each ticket unpacked into steps:
files, types, verification commands. Order follows the dependency graph (native GitHub
dependencies); the frontier at the start is #2 and #3.

Approved by the owner 2026-09-25. The detailed per-ticket plan approved 2026-09-25.

---

## Phase 1. MVP, by ticket

### #2. Deploy the skeleton to Coolify and set up CI — no blockers

The database reuses the PostgreSQL instance already running on the VPS: create a dedicated role
and database for this project instead of provisioning a new Coolify PostgreSQL resource. Coolify:
a Docker Compose resource from the repository (branch `main` after merging
[PR #19](https://github.com/mykola-blonskyi/taxes-ua/pull/19)), the domain → `web` service, `api`
with no external port, `DATABASE_URL` pointing at the new role/database in env. If the existing
Postgres instance has no backup already, add the project's own scheduled logical dump (ADR-006).
`.github/workflows/ci.yml`: `dotnet test api`, `pnpm --dir web lint && pnpm --dir web build`,
`docker build ./api` and `./web`.

Verify: `curl https://<domain>/api/health` → `{"status":"ok","database":true}`; `api` is not
reachable from outside; the new role can connect only to its own database; the workflow fails on
a broken test.

Relies on: ADR-006, ADR-001.

---

### #3. Google sign-in and the interface shell — no blockers

`api/src/TaxesUa.Api/Data/AppDbContext.cs` → `IdentityDbContext<ApplicationUser>`,
`SchemaVersion = IdentitySchemaVersions.Version3`. `api/src/TaxesUa.Api/Identity/
ApplicationUser.cs` (new). `Program.cs`: `AddIdentityCore`, `AddGoogle`, an allowlist middleware
from `Auth:AllowedEmails`, `/api/auth/login/google`, `/api/auth/callback`, `/api/auth/logout`,
`/api/auth/me`. Migration `AddIdentity`.

`web/src/i18n/` — next-intl config, `web/messages/uk.json`, `web/messages/ru.json`.
`web/src/shared/theme/` — `ThemeProvider` (`next-themes`), tokens from the prototype.
`web/src/shared/ui/` — shadcn/ui (`pnpm dlx shadcn@latest init`, alias `@/shared/ui`).
`web/src/app/layout.tsx` — wraps `ThemeProvider` + `NextIntlClientProvider`.
`web/src/app/(app)/layout.tsx` — navigation (mobile bottom bar / desktop sidebar), the disclaimer
(Rule 11). `web/src/data/auth/useMe.ts` — redirects to `/login` on 401.

Verify: signing in with an allowlisted email creates a session, `/api/auth/me` returns the user;
another email gets 403; the cookie is `HttpOnly; Secure; SameSite=Lax`; theme and language (uk/ru)
toggle and persist; every MVP placeholder screen opens at 375px with no horizontal scroll.

Relies on: ADR-005, Rule 11, domain-model `User`.

---

### #4. Year parameters and FOP settings — blocked by #3

`api/src/TaxesUa.Api/Entities/TaxYearConfig.cs`, `Settings.cs` (new, fields from domain-model).
`AppDbContext`: `DbSet<TaxYearConfig>`, `DbSet<Settings>`. Migration
`AddTaxYearConfigAndSettings` + a 2026 seed (values from Rule 3/Rule 4) at startup.
`Endpoints/SettingsEndpoints.cs`, `TaxYearEndpoints.cs`: `GET/PUT /api/settings`,
`GET /api/tax-years`, `PUT /api/tax-years/{year}`, `POST /api/tax-years/{year}/verify`,
`POST /api/tax-years/{year}/clone-to/{next}`. Recompute `EsvMonthlyKop` and `IncomeLimitKop` on
save via `Money.ApplyBp`/similar.

`web/src/features/settings/` — `index.ts`, `components/FopSettingsForm.tsx`,
`components/TaxYearTable.tsx`, `hooks/useSettings.ts`, `hooks/useTaxYears.ts`.
`web/src/data/settings/`, `web/src/data/tax-years/`. `web/src/app/(app)/settings/page.tsx`.

Verify: `GET /api/tax-years/2026` → `EsvMonthlyKop=190234`, `IncomeLimitKop=1009104900`; a test
for the derived-field recomputation; cloning 2026→2027 leaves `VerifiedAt=null`.

Relies on: ADR-007, Rule 3, Rule 4, Rule 9, domain-model `Settings`/`TaxYearConfig`.

---

### #5. Receipts in hryvnia — blocked by #4

`api/src/TaxesUa.Api/Entities/Client.cs`, `Transaction.cs` (new: `AmountMinor: long`,
`Currency`, `RateE4: int = 10000`, `AmountUahKop: long`, `Kind`, `NonIncomeReason`). Migration
`AddClientsAndTransactions`, index `(UserId, ValueDate)`. `Endpoints/TransactionsEndpoints.cs`:
`GET/POST/PUT/DELETE /api/transactions`, filter `?year=`, validation (amount > 0,
`NonIncomeReason` required when `Kind != Income`), UAH via `Money.ToUahKop` with `RateE4=10000`.
A warning when `ValueDate < Settings.FopRegistrationDate` (`warnings[]` in the response).

`web/src/features/transactions/` — `index.ts`, `components/TransactionForm.tsx`,
`components/TransactionTable.tsx`, `hooks/useTransactions.ts`. `web/src/data/transactions/`.
`web/src/app/(app)/transactions/page.tsx`.

Verify: `dotnet test` — amount ≤ 0 and non-income without a reason validate as 400 (set up
`tests/TaxesUa.Api.Tests` in this ticket if not already there); CRUD from the screen; the year
total equals Σ Income − Σ RefundToClient; an operation before registration shows a warning.

Relies on: Rule 1, Rule 8, Rule 10, ADR-003, domain-model `Transaction`/`Client`.

---

### #7. Deadline calendar — blocked by #4 (can run alongside #5/#6)

`api/src/TaxesUa.Engine/DeadlineCalendar.cs` (new): `ForQuarter(int year, int quarter,
TaxYearConfigInput config, FopSettingsInput settings)` → statutory and shifted dates for ESV,
declaration, payment. Records `TaxYearConfigInput`, `FopSettingsInput` in the Engine.
`tests/DeadlineCalendarTests.cs`: a table-driven test against the 2026 reference (Rule 5), tests
for both values of `TaxPaymentCountsFromStatutoryDeclarationDate` and
`ShiftTaxPaymentFromWeekend`, and a test with a holiday.

`Endpoints/ObligationsEndpoints.cs` (stub, completed in #8): `GET /api/periods/{year}` returns
the deadline dates. `web/src/features/periods/components/DeadlinesTable.tsx` (partial).

Verify: `dotnet test --filter DeadlineCalendarTests` — all 12 dates of the 2026 reference match
(2026-04-20/2026-05-11/2026-05-20, 2026-07-20/2026-08-10/2026-08-19,
2026-10-19/2026-11-09/2026-11-19, 2027-01-19/2027-02-09/2027-02-19); Q4 yields 2027 dates.

Relies on: Rule 5 in full, ADR-002 (no `DateTime.Now`, only config as input).

---

### #6. Currency receipts and the NBU rate — blocked by #5

`api/src/TaxesUa.Api/Entities/FxRate.cs` (new), key `(Currency, Date)`.
`External/NbuRateClient.cs` (new): `HttpClient`, 5s timeout, falls back day by day up to 7 tries
on an empty `[]`. `Endpoints/FxEndpoints.cs`: `GET /api/fx?currency=USD&date=2026-09-26`. Extend
`TransactionsEndpoints`: non-UAH requires `RateE4`, `RateSource: Nbu | Manual`.
`web/src/data/fx/useFxRate.ts` — auto-fills in `TransactionForm.tsx` when date/currency changes.

Verify: an `NbuRateClient` test with a substituted `HttpMessageHandler` — an empty Saturday
response returns Friday's rate with `RateDate` set to Friday; a repeat request is served from the
`FxRate` cache, not the handler; NBU unavailability → 502 with a message, the form still allows a
manual entry. By hand: enter 100 USD dated on a Saturday, see Friday's rate with the date noted.

Relies on: Rule 2, ADR-003, domain-model `FxRate`.

---

### #8. Accruals and the declaration numbers — blocked by #6, #7

`api/src/TaxesUa.Engine/IncomeLedger.cs` (new): income by month/quarter/cumulative, excluding
operations before `FopRegistrationDate` with an `EngineWarning`. `Engine/Accruals.cs` (new):
`ForYear(...)` — EP/VZ cumulative minus already accrued (Rule 3), ESV over active months per
`EsvRegistrationMonthPolicy`/`EsvExempt`. Tests: registration mid-quarter (1 and 2 active
months), a refund in a different quarter, a refund larger than the month's income, `EsvExempt`,
the sum of quarterly EP equals cumulative EP for the year.

`Endpoints/PeriodsEndpoints.cs`: `GET /api/periods/{year}` in full (months, quarters, cumulative
total, deadlines from #7), a stub warning about an unverified year in the dashboard.
`web/src/features/periods/` — `index.ts`, `components/QuartersTable.tsx`,
`components/DeclarationNumbers.tsx`, `hooks/usePeriods.ts`. `web/src/app/(app)/periods/page.tsx`.

Verify: engine scenario tests pass; the API response matches the engine's computation on the same
data; the tables match the prototype's column layout.

Relies on: Rule 1, Rule 3, Rule 8, ADR-002.

---

### #9. Budget payments and balances — blocked by #8

`api/src/TaxesUa.Api/Entities/BudgetPayment.cs` (new: `Kind`, `AmountKop`, `PeriodYear`,
`PeriodQuarter?`, `PeriodMonth?`). `Engine/Balances.cs` (new): accrued cumulative minus paid per
kind, overpayment carried forward within a kind, kinds never mixed. `Engine/
ObligationBuilder.cs` (new): assembles `Obligation[]` with statuses relative to `today` (a
parameter, not `DateTime.Now`). Tests: an ESV overpayment covers the next quarter; an EP
overpayment never offsets an ESV debt; statuses `Upcoming`/`Due`/`Overdue`/`Done`.

`Endpoints/PaymentsEndpoints.cs`: CRUD `/api/payments`, period validation. `web/src/features/
payments/` — form, list, balances panel. `web/src/app/(app)/payments/page.tsx`; "paid"/
"remaining" columns in `QuartersTable`.

Verify: engine table-driven tests pass; CRUD through the API and the screen; balances show an
overpayment with the right sign.

Relies on: Rule 7, domain-model `BudgetPayment`/`Obligation`.

---

### #10. Home screen: the next step — blocked by #9

`Engine/ObligationBuilder.cs` (extension) — `NextStep(today)`. `Endpoints/
DashboardEndpoints.cs`: `GET /api/dashboard`, "today" computed by `Europe/Kyiv` via
`TimeZoneInfo` (not UTC). Tests: picking the next step with several obligations/an overdue
item/an empty list/a date before registration; the Kyiv-midnight vs UTC boundary.

`web/src/features/dashboard/` — `components/HeroCard.tsx` (states: normal, overdue, clear,
before registration), `hooks/useDashboard.ts`. `web/src/app/(app)/page.tsx`.

Verify: a test for the Kyiv-midnight boundary; the hero matches the prototype; marking a payment
updates the step without a reload (TanStack Query invalidate).

Relies on: ADR-004, Rule 5.

---

### #11. Income limit — blocked by #10

`Engine/LimitMonitor.cs` (new): percentage, 85/100 thresholds, excess, tax at `ExcessRateBp`.
Tests: 84.99%, 85%, 100%, excess by one kopeck. Add a `limitStatus` field to
`DashboardEndpoints`. `web/src/features/dashboard/components/LimitBar.tsx`.

Verify: tests at the thresholds; the bar changes color/text at 85% and 100%.

Relies on: Rule 4.

---

### #12. Monthly advances — blocked by #10

`Engine/Accruals.cs` (extension) — monthly accruals. Tests: an advance larger than the quarterly
accrual → an overpayment; a partial advance → a remainder. `web/src/features/periods/components/
MonthsTable.tsx` (shown only when `PaymentMode == MonthlyAdvance`). `BudgetPayment` forms — a
"month" period.

Verify: switching modes never changes accruals, only recommendations; the months table matches
the prototype.

Relies on: Rule 6.

---

### #13. CSV and XLSX export — blocked by #6

`Endpoints/ExportEndpoints.cs`: `GET /api/export/transactions.csv|xlsx` (ClosedXML, add to
`Directory.Packages.props`). `web/src/features/transactions/components/ExportButtons.tsx`.

Verify: both files open cleanly in Excel/Numbers, Cyrillic survives (BOM for CSV), hryvnia
amounts with two decimals match the screen.

---

### #14. JSON backup and restore — blocked by #9

`Endpoints/BackupEndpoints.cs`: `GET /api/backup`, `POST /api/restore` (a database transaction,
replacing the user's data). `web/src/features/backup/` — buttons, a confirmation before
replacing.

Verify: backup → wipe → restore yields identical data across every MVP entity; an invalid file →
400, nothing changes.

---

### #15. Prototype JSON import — blocked by #12

`Endpoints/ImportEndpoints.cs`: `POST /api/import/prototype` — `incomes → Transaction
(RateSource: Manual)`, `mpaid → BudgetPayment × 3` (EP/VZ/ESV, "month" period). Idempotence via
tagging imported records with a source external key.

Verify: re-importing the same file doesn't change the record count.

---

### #16. Passkey — blocked by #3

`Program.cs`: `Configure<IdentityPasskeyOptions>` (`ServerDomain`), the
`PasskeyCreationOptions`, `PerformPasskeyAttestationAsync`, `PerformPasskeyAssertionAsync`
endpoints. `web/src/features/auth/components/PasskeyButton.tsx` —
`navigator.credentials.create/get`.

Verify: passkey registration and sign-in on iOS Safari, Android Chrome, desktop; an invalid
attestation is rejected.

Relies on: ADR-005.

---

### #17. Change log — blocked by #9

`Data/AuditSaveChangesInterceptor.cs` (new) — before/after snapshots in jsonb for
Transaction/BudgetPayment/Settings/TaxYearConfig. `Endpoints/AuditEndpoints.cs`:
`GET /api/audit?entity&id`. `web/src/features/audit/components/HistoryPanel.tsx`.

Verify: create → edit → delete a transaction yields three log entries with correct snapshots.

---

### #18. PWA and mobile polish — blocked by #3

`web/public/manifest.json`, icons, a Serwist service worker.

Verify: Lighthouse marks the app installable; it installs and runs standalone on iOS/Android; no
screen scrolls horizontally at 375px.

---

Tickets #13, #16, #17, #18 are off the critical path (#2 → #3 → #4 → #5 → #7 → #6 → #8 → #9 →
#10) and can be done whenever convenient after their blocker.

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
