# Dependency Analysis

Measured from the compiled code on 2026-10-04 (#185). The feature table below is the allow-list in
`api/tests/TaxesUa.Api.Tests/Architecture/FeatureBoundaryTests.cs`; that test fails when the two differ,
and prints the table to paste here.

## Internal Dependencies

### Projects

- `TaxesUa.Api` references `TaxesUa.Engine`. The engine references no package and no other project.
- `TaxesUa.Api.Tests` references `TaxesUa.Api`; `TaxesUa.Engine.Tests` references `TaxesUa.Engine`.
- `web` reaches the api only over HTTP, through types generated from its OpenAPI document
  (`web/src/data/api/schema.d.ts`). CI fails when they differ (`.github/scripts/check-api-schema.sh`).

### API features

A feature reaches only the features on its row. A layer is one more than the highest layer the feature
reaches, so every edge points down and no cycle can form. Every feature may also use the shared code in
`TaxesUa.Api` (`Problems`, `TextRules`, `KyivTime`, `Incident`) and `TaxesUa.Api.Data` (`AppDbContext`,
`OwnerLock`), which itself reaches no feature, `AppDbContext` aside.

| Feature | Layer | Reaches |
| --- | --- | --- |
| Auth | 0 | nothing |
| DatabaseBackups | 0 | nothing |
| Fx | 0 | nothing |
| TaxYears | 0 | nothing |
| Banking | 1 | Fx |
| Settings | 2 | Auth, Banking, Fx, TaxYears |
| Payments | 3 | Auth, Banking, Settings |
| Transactions | 3 | Auth, Banking, Fx, Settings, TaxYears |
| Export | 4 | Auth, Fx, Settings, Transactions |
| Monobank | 4 | Auth, Banking, Fx, Payments, Settings, Transactions |
| Periods | 4 | Auth, Payments, Settings, TaxYears, Transactions |
| Declarations | 5 | Auth, Payments, Periods, Settings, TaxYears, Transactions |
| Invoices | 5 | Auth, Export, Fx, Settings, Transactions |
| Audit | 6 | Auth, Declarations, Invoices, Payments, Settings, TaxYears, Transactions |
| Clients | 6 | Auth, Fx, Invoices, Transactions |
| Dashboard | 6 | Auth, Declarations, Invoices, Monobank, Payments, Periods, Settings, TaxYears, Transactions |
| Notifications | 6 | Auth, Declarations, Export, Periods, Settings |
| Backup | 7 | Audit, Auth, Banking, Declarations, Fx, Invoices, Monobank, Notifications, Payments, Periods, Settings, TaxYears, Transactions |
| Calendar | 7 | Auth, Notifications, Periods, Settings, TaxYears |

82 edges over 19 features. Before #185 the same measurement found 85 edges, and ten features
(Declarations, Export, Invoices, Monobank, Notifications, Payments, Periods, Settings, TaxYears,
Transactions) formed one cycle. ADR-008's amendment lists what moved to break it.

The test reads type references from IL, so a reference made only through a `const` (inlined by the
compiler) does not show. `TransactionsEndpoints.MinYear` and `MaxYear`, read by Payments, Settings,
Invoices, Export and Backup, are the main case.

### Web layers

`app` → `features` → `data` → `shared`, enforced by `no-restricted-imports` in `web/eslint.config.mjs`.
Features never import each other.

## External Dependencies

### API runtime packages (`api/Directory.Packages.props`)

- `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.AspNetCore.Authentication.Google`:
  sign-in (ADR-005).
- `Npgsql.EntityFrameworkCore.PostgreSQL`: the database. `Microsoft.EntityFrameworkCore.Design` is
  build-time only, for migrations.
- `Microsoft.AspNetCore.OpenApi`: the OpenAPI document, served in Development only.
- `MailKit`: email reminders and the address check (ADR-022).
- `DocumentFormat.OpenXml`: the XLSX export. `PDFsharp-MigraDoc`: the PDF export and invoices.

Test-only: xunit, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql`,
`Microsoft.Extensions.TimeProvider.Testing`, `PdfPig` and `Ical.Net` (to read back what the api writes).

### Web runtime packages (`web/package.json`)

`next`, `react`, `react-dom`, `next-intl`, `next-themes`, `@tanstack/react-query`, `openapi-fetch`,
`radix-ui` and `shadcn` with `class-variance-authority`, `cn`, `lucide-react`, `tw-animate-css`, and
`qrcode-generator` for the NBU payment QR code. TanStack Table and TanStack Form are not used.

### External services

- PostgreSQL 16, the shared instance on the VPS (ADR-006).
- NBU exchange-rate API, read through `Features/Fx/NbuRateClient`, cached in `FxRates`.
- monobank personal API, one statement call per owner per 60 seconds (`MonobankRateGate`).
- Telegram Bot API, by long polling (ADR-015). SMTP for email (ADR-022).
- Google OAuth for sign-in.

## Dependency Risks

- monobank and NBU can be slow, down or rate-limited. The sync records the failure and alerts once per
  incident (ADR-026); a missing NBU rate answers 502 and the owner retries.
- The monobank queue, rate gate and client-info cache live in one process. A second api instance would
  double the bank calls; the api must run as one replica.
- Google is the only first sign-in. A passkey, once registered, signs the owner in without it.
- `xunit` is still on v2.
