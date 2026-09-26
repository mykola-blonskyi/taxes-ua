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
- Background jobs as `IHostedService`: reminders, bank-sync queue (Stage 2).
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
  Returns `[]` on weekends. The adapter falls back to the last business day and stores the actual
  rate date. Responses are cached in the `fx_rate` table.
- monobank personal API, PrivatBank Autoclient (Stage 2). Tokens are encrypted with AES-256-GCM
  using a key from the environment.
- Telegram Bot API and SMTP for reminders (Stage 2).
- DPS XML declaration schema F0103309 (Stage 3).

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
    <Name>/       payments, periods, dashboard, export, backup, audit)
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
- Database: a PostgreSQL instance already running on the VPS is reused. A dedicated role and
  database are created for this project instead of provisioning a new Coolify PostgreSQL
  resource. Backups: whatever backup mechanism already covers that instance, plus the project's
  own scheduled logical dump if that instance has none.
- Cron: hosted services inside `api`. No external scheduler is needed.
- Secrets: Coolify environment variables. `.env.example` in the repository holds no values.
- Cost: 0.

---

## Security

Authentication: ASP.NET Core Identity, Google as the external sign-in, passkey as a second
method. Sign-in is allowed only for the email in `Auth__AllowedEmails`. Cookie
`HttpOnly; Secure; SameSite=Lax`.

Authorization: every read and write is filtered by the `UserId` from the session.

Secrets management: the bank-token encryption key lives only in the environment. Tokens are
decrypted at the moment of the bank API call and never appear in logs, responses or the client.

Other: HTTPS via Traefik. Anti-forgery for cookie auth via the `X-Requested-With` header and
SameSite. Change log `audit_log`. In-app disclaimer: the calculation is informational.

---

## Observability

Logging: structured ASP.NET Core logs to stdout, read through Coolify. Amounts are logged; tokens
and emails are not.

Metrics: not needed for a single user. `/api/health` with a database check, for Coolify
monitoring.

Tracing: none.
