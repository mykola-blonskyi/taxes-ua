# Architecture

## Overview

A personal web application for tracking income and taxes of a Group 3 FOP (single tax 5%, no
VAT). Tracks foreign-currency receipts converted at the NBU rate, computes EP, VZ and ESV, shows
deadlines, keeps a ledger of budget payments with a balance per kind, and prepares the numbers for
the declaration.

The application does not pay taxes and does not file declarations. Payments are made by the user
in their bank; the declaration is signed with a KEP in the Electronic Cabinet.

Full spec: `/Users/mykola/Documents/obsidian-notes/tsxes-ua/SPEC.md` (outside the repository).

---

## Goals

- Exact calculation from year parameters, no hardcoding. A new year needs no code change.
- The home screen answers one question: what to do next, by when, how much.
- Single user for now. The data model carries `UserId` so the product can later open up to other
  FOPs.
- Free, self-hosted deployment on the existing VPS. No paid services.

---

## Stack

| Layer | Choice | Why |
| --- | --- | --- |
| Backend | ASP.NET Core 10 (LTS), Minimal APIs, C# | Owner's decision ([ADR-001](decisions.md)). Strong typing, hosted services for cron, built-in OpenAPI. |
| ORM | EF Core 10 + Npgsql, migrations | Platform standard. |
| Auth | ASP.NET Core Identity + Google OAuth + passkey (built into Identity in .NET 10), cookie session, email allowlist | Free, no vendor. 2FA comes from the Google account. |
| Tax engine | `TaxesUa.Engine`, a package-free class library, xUnit | Testable without a database or UI. |
| Frontend | Next.js (App Router), TypeScript | Owner's preference. UI only, no server code. |
| Client | TanStack Query, Table, Form; types generated from OpenAPI via `openapi-typescript` | One source of types, the API contract is never hand-duplicated. |
| UI | Tailwind CSS + shadcn/ui, next-intl (uk by default, ru), PWA via Serwist | Responsive layout, light and dark theme, installable on a phone. |
| State | Zustand only when actually needed | No global client state in the MVP. |
| Database | PostgreSQL 16+ | Owner's preference. Already running on the VPS. |
| Deploy | Coolify on the `blonskyi-dev` VPS, Docker Compose from the repository | Traefik with auto-TLS, existing PostgreSQL instance, backups. |

---

## Components

### api (ASP.NET Core)

Responsibilities:

- REST API: transactions, payments, settings, year parameters, periods, obligations, export,
  backup.
- Authentication and sessions. Every data request is filtered by `UserId`.
- Boundary adapters: import parsing, Europe/Kyiv date conversion, NBU rate, conversion to
  kopecks, validation.
- Background jobs as `IHostedService`: reminders every 5 minutes (ADR-019), bank-sync queue, Telegram
  polling (Stage 2).
- Change log.

Dependencies: `TaxesUa.Engine`, PostgreSQL, the NBU API, later the Telegram Bot API, SMTP, bank
APIs.

### TaxesUa.Engine (class library)

Responsibilities:

- Income by period, accounting for refunds.
- EP, VZ, ESV accruals by quarter and by month, the cumulative total for the declaration.
- Deadlines with weekend shifting under configurable rules.
- Balances per payment kind, overpayments and remainders, recommended advances.
- Income-limit monitoring with 85% and 100% thresholds and the excess rate.
- Warnings: operations before the registration date, a year without verified parameters.
- Invoice matching: which open invoices an imported receipt may be paying (`InvoiceMatcher`, Rule 14).

Dependencies: none. Input is plain data only: `DateOnly`, `long` kopecks, enums, records.

### web (Next.js)

Responsibilities: screens, forms, PWA, themes, i18n. Calls the API through `/api/*`, which
Next.js rewrites to the `api` container. From the browser this is a single origin, so the cookie
session works without CORS.

Dependencies: `api`.

### PostgreSQL

Stores the entities from [knowledge/domain-model.md](../knowledge/domain-model.md). Money as
`bigint` kopecks, rates as integers scaled by 10⁴, operation dates as `date` in Kyiv time.

### Integrations

External systems:

- NBU: `https://bank.gov.ua/NBUStatService/v1/statdirectory/exchange?valcode=USD&date=YYYYMMDD&json`.
  Returns `[]` for a date it has not published (checked 2026-09-26: weekends currently come back
  labelled with their own date, an unpublished future date comes back `[]`). The adapter falls back
  day by day, up to 7 days, and stores the actual rate date. Responses are cached in the `FxRates`
  table, except for a future date, whose fallback is provisional.
- monobank personal API (Stage 2). PrivatBank is deferred: the owner has no FOP account there.
  Tokens are encrypted with AES-256-GCM
  using a key from the environment.
- Telegram Bot API (Stage 2, #106): the token is configuration (`TELEGRAM_BOT_TOKEN`), optional. `TelegramClient` is
  a typed HttpClient registered without the framework's request logging, because the Bot API puts the token in
  the URL path. `TelegramPollWorker` long-polls `getUpdates` when a token is set (ADR-015); `TelegramDelivery` is
  the one way a message is sent (the test button and the reminders): three retries after the first attempt with
  1, 2 and 4 second backoff, honouring a 429's `retry_after` up to 30 seconds, a 403 switching the channel off.
  `Telegram:BaseUrl` (default `https://api.telegram.org/`) exists so a local run can point at a stub.
  Reminders (#108, ADR-019) reach it through `IReminderChannel`, the boundary email (#107) plugs into, and
  link to `App:PublicUrl` (`APP_PUBLIC_URL`), or `https://` and the first `ALLOWED_HOSTS` domain when unset.
- SMTP for reminders (Stage 2).
- DPS XML schemas F0103309 (the declaration) and F0133109 (its ESV annex), vendored and embedded (ADR-016, ADR-018).

---

## Data flow

```
browser (Next.js UI)
   │  /api/*  (same origin, rewrite → http://api:8080)
   ▼
api: boundary
   UTC → Europe/Kyiv date, amount → kopecks, NBU rate → RateE4, validation
   │
   ▼
PostgreSQL (transaction, budget_payment, settings, tax_year_config, …)
   │
   ▼
TaxesUa.Engine (pure functions: obligations, balances, periods, limit)
   │
   ▼
JSON API responses → screens, export, reminders
```

The engine recomputes everything on every request. One FOP's data volume is a few hundred rows a
year; no cache is needed.

---

## Repository layout

```
api/                          .NET solution
  Directory.Build.props       shared compilation properties
  Directory.Packages.props    package versions in one place (Central Package Management)
  TaxesUa.slnx
  src/TaxesUa.Engine/         the engine, no packages
  src/TaxesUa.Api/            ASP.NET Core
  tests/TaxesUa.Engine.Tests/
  tests/TaxesUa.Api.Tests/
  Dockerfile
web/                          Next.js
  messages/uk.json, ru.json   next-intl translations
  src/                        see below
  Dockerfile
docker-compose.yml            for Coolify
docs/ knowledge/ plans/       documentation
```

### api layers

```
src/TaxesUa.Api/
  Program.cs      composition root: DI, middleware, calls each feature's Map<Name>Api()
  Data/
    AppDbContext.cs   only DbSet<T> per entity, no business logic
    Migrations/
  Features/       one directory per resource (auth, settings, tax-years, transactions, fx,
    <Name>/       payments, periods, declarations, dashboard, export, backup, audit, invoices)
      <Entity>.cs        EF entity/entities, declared internal
      <Name>Endpoints.cs the feature's single public surface: Map<Name>Api(this
                         IEndpointRouteBuilder group), called once from Program.cs
```

The dependency rule is enforced by the C# `internal` access modifier (ADR-008): entities and
feature-internal helpers are `internal`, so only a feature's `Map<Name>Api` method is visible to
`Program.cs` and to other features — the compiler refuses a feature that reaches into another
feature's types, the same way eslint refuses it on the frontend. `TaxesUa.Engine` (ADR-002)
sits outside this tree entirely, as a separate, package-free project; any feature that needs a
computation references it, never duplicates it. `AppDbContext` and the audit save interceptor
are the two deliberate exceptions — EF Core needs one `DbContext`, and change auditing needs to
see every audited entity, so both necessarily touch every feature.

**Change log.** `Features/Audit/AuditSaveChangesInterceptor` is the only writer of `AuditLog`. On
every `SaveChangesAsync` it snapshots each added, modified or deleted `Transaction`,
`BudgetPayment`, `Settings` and `TaxYearConfig` into one entry, in the same database transaction as
the change. The audited types are an opt-in list, so Identity's rows (password hashes, security
stamps, passkeys) never reach the log. Because it reads the change tracker, an audited table must be
written through tracked entities: `ExecuteUpdate`, `ExecuteDelete` or raw SQL against one of these
tables bypasses the log. A database trigger makes `AuditLog` append-only.

**Dismissed imports.** Deleting an imported transaction keeps the row as `ReviewStatus.Dismissed`
(Rule 12). A global query filter on `Transaction` hides dismissed rows, so every read path (lists,
periods, the dashboard, exports, refund links) leaves them out without asking. Only the code that
must see a tombstone opts out with `IgnoreQueryFilters`: the sync's duplicate check and currency-sale
pairing, the backup, and the restore's delete and id check.

A restore from backup is not the owner's edits, so it writes one summary entry
(`AuditEntry.Restored`, entity `Backup`, action `Restore`, with the restored counts) in the same
save as the rows it inserts. A save that carries that summary gets no per-row entries. The log is
history, not state: a backup does not carry it and a restore does not replace it.

A prototype import (`POST /api/import/prototype`) merges rather than replaces, so it takes the
ordinary path: one `Create` entry per inserted row. It shares the restore's per-owner advisory lock,
and its dry run is the same code in a transaction that is rolled back.

**Bank sync.** `POST /api/monobank/sync` only enqueues the owner's followed FOP accounts on
`MonobankSyncQueue`; the request never calls the bank. One `BackgroundService`,
`MonobankSyncWorker`, takes one account at a time. It waits its turn at `MonobankRateGate` (one
statement call per owner per 60 seconds, timed on `TimeProvider` so tests advance a fake clock),
reads the last 31 days, and records each settled credit through `TransactionRecorder`, the operation
behind `POST /api/transactions`, with the row's import provenance (Rule 12). Each account is imported
in one database transaction under the restore's per-owner advisory lock, so a sync never interleaves
with a restore, and every inserted row gets its ordinary `Create` entry. The queue lives in memory: a
restart drops queued work, and the owner presses "sync now" again. The queue and the gate are per
process, so the api runs as one instance (#77 adds a persisted cursor and
the 429 and 401 handling, both at the gate and the worker). `BankAccount`, `ImportBatch` and the
connection are not audited: an account snapshot would put the full IBAN on the History screen.

**Reserve jar.** After each sync run the worker refreshes the balance of the owner's chosen jar (#102,
ADR-021) with `client-info`, read by `MonobankJarReader` through the same `MonobankClient` and a
`client-info` slot of `MonobankRateGate`. The reader takes the slot only when it is free and reuses its
last answer for the 60-second interval, so a sync run, the owner's refresh and the jar list never make a
second call within a minute; the dashboard reads the stored row and never calls the bank.

### web layers

```
src/
  app/          routes and layout only. Imports features, shared, data. Nothing imports app.
  data/         DAL: fetch client, types generated from OpenAPI, TanStack Query query options
                and mutations per API resource. Imports only shared.
  features/     one directory per user-facing capability (transactions, payments,
    <name>/     dashboard, periods, settings, auth, backup)
      components/
      hooks/    hooks on top of data: assemble queries and mutations for the feature's scenario
      tests/
      index.ts  the feature's single public entry point
  shared/       the bottom layer. Imports nothing from app, features or data
    lib/        utilities: cn, money and date formatting
    ui/         shadcn/ui components (alias @/shared/ui in components.json)
    types/      hand-written types unrelated to the API
    constants/
    shell/      app chrome shared by every route group: navigation, header, disclaimer, the
                theme and language toggles
    theme/      ThemeProvider and the theme toggle. The colour tokens themselves live in
                app/globals.css, because Tailwind v4 keeps the theme in CSS
  i18n/         next-intl configuration, locale chosen from a cookie
```

The dependency rules are enforced in eslint (`no-restricted-imports`): features never import
each other and are only reachable from outside through `index.ts`; `data` knows nothing about
features; `shared` imports nothing above itself; nothing imports from `app`.

Both layouts follow the same idea — one folder per feature, reachable only through its public
surface — enforced by whatever each language gives for free: `internal` in C#, `no-restricted-
imports` in eslint.

---

## Deployment

- Host: VPS `blonskyi-dev`, Ubuntu 24.04, 4 vCPU, 7.7 GB RAM, Docker 29, Coolify with Traefik v3.
- Application: a Coolify Docker Compose resource built from the GitHub repository, services `web`
  and `api`. The domain points at `web`. `api` is not published externally.
- Database: Coolify's `shared-database` PostgreSQL instance, already running on the VPS, is
  reused. The login role `taxes_ua_app` owns the database `taxes_ua`, the convention every
  project on that instance follows ([ADR-006](decisions.md)). Backups: Coolify's daily
  instance-wide dump, kept on the VPS and in the owner's MinIO.
- Cron: hosted services inside `api`. No external scheduler is needed.
- Secrets: Coolify environment variables. `.env.example` in the repository holds no values, and
  the api refuses to start outside Development while a required one is empty.
- Sessions: the data-protection key ring is persisted in the `dataprotection-keys` volume, so a
  redeploy keeps the owner signed in ([ADR-010](decisions.md)).
- Runbook: [`docs/deploy.md`](deploy.md).
- Cost: 0.

---

## Security

Authentication: ASP.NET Core Identity, Google as the external sign-in, passkey as a second
method. Sign-in requires an email listed in `Auth__AllowedEmails`, and Google sign-in additionally
requires that Google report it verified. The allowlist governs both methods: a passkey is registered
by an already-signed-in owner, and the allowlist is re-checked against the asserted user's email on
every passkey sign-in, because a stored credential outlives the email's removal from the list.
`Features/Auth/PasskeyEndpoints.cs` says why that re-check is the last place it can happen. The
session cookie, the external sign-in cookie and the passkey ceremony cookie are all
`HttpOnly; Secure; SameSite=Lax`. A session cannot be revoked server-side, which
[ADR-009](decisions.md) explains.

Authorization: every read and write is filtered by the `UserId` from the session.

Secrets management: the bank-token encryption key lives only in the environment. Tokens are
decrypted at the moment of the bank API call and never appear in logs, responses or the client.

Other: HTTPS via Traefik. `ALLOWED_HOSTS` pins the host the Google redirect URI is built from, and
the api refuses to start in Production without it. Host filtering sees the host the container was
addressed by, so outside Development it also accepts `api` and `localhost`; the pin to the domain is
`ForwardedHeadersOptions.AllowedHosts`, and a forwarded host outside it gets 400. `web/next.config.ts`
sends HSTS, `nosniff`, `X-Frame-Options: DENY`, a referrer policy and a Content-Security-Policy; the
api sends the same headers except the CSP on `/api/*`, which Next passes through untouched. `PASSKEY_SERVER_DOMAIN` pins the WebAuthn Relying
Party ID rather than letting Identity infer it from the host header, and the api refuses to start
without it too; a passkey is bound to the RP ID it was registered against. The OpenAPI document is
served only in Development. Anti-forgery for cookie auth via the `X-Requested-With` header and
SameSite. Change log `audit_log`. In-app disclaimer: the calculation is informational.

---

## Observability

Logging: structured ASP.NET Core logs to stdout, read through Coolify. Amounts are logged; tokens
and emails are not.

Metrics: not needed for a single user. `/api/health` with a database check, for Coolify
monitoring.

Tracing: none.
