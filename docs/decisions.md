# Architecture Decisions

Format: context, decision, alternatives considered, consequences. Dates are absolute.

---

## ADR-001. Backend on ASP.NET Core, frontend on Next.js

Date: 2026-09-25

Status: Accepted

### Context

Three options for the server layer were considered: Next.js only (Server Actions), a separate
NestJS service, a separate .NET service. The application is single-user; the server-side logic is
CRUD, export, external APIs and cron. Hosting is a VPS with Coolify.

### Decision

A separate backend on ASP.NET Core 10 (Minimal APIs, EF Core, Identity). Next.js stays a pure
interface and proxies `/api/*` to the backend. The owner made this call after understanding the
trade-off below.

### Alternatives Considered

Next.js only. One language, one deployment, minimum infrastructure. The implementer's
recommendation, declined.

NestJS. Duplicates what Next.js already provides, a second TS deployment with no type-sharing
benefit.

### Consequences

Pros: strong typing and `DateOnly`/`long` for money, hosted services instead of an external cron,
Identity with Google and passkey out of the box, built-in OpenAPI. Cons: two languages and two
builds; UI types are generated from OpenAPI (`openapi-typescript`) so they don't drift from the
backend. A second container on the shared VPS, roughly 150 MB RAM.

---

## ADR-002. Tax engine as a separate package with no dependencies

Date: 2026-09-25

Status: Proposed

### Context

The spec requires a pure module with full test coverage of the key scenarios before any UI
exists.

### Decision

`api/src/TaxesUa.Engine` as a class library with no `PackageReference`. Input and output are
plain data only: `DateOnly`, `long` kopecks, enums, records. No `DateTime.Now`, no database, no
network, no locale. Tests live in `tests/TaxesUa.Engine.Tests` on xUnit.

### Alternatives Considered

A folder inside the Api project. Cheaper, but the boundary is held by discipline alone. A
separate, package-free project turns a boundary violation into a build error.

### Consequences

Engine tests run in milliseconds, with no environment. The application must prepare the data at
the boundary.

---

## ADR-003. Money as whole kopecks, rate as an integer scaled by 10⁴

Date: 2026-09-25

Status: Proposed

### Context

The spec forbids floats. The NBU rate has 4 decimal places (e.g. 44.9729).

### Decision

Amounts are stored and computed in the currency's minor unit as `bigint` in the database, `long`
in C#, and `number` in TS (JSON numbers are safe up to 2⁵³, i.e. up to 90 trillion hryvnia). The
rate is stored as `rateE4 = round(rate × 10⁴)`. Hryvnia equivalent:
`kopecks = roundHalfUp(minorUnits × rateE4 / 10⁴)`. One rounding per operation. Percentages are
stored as basis points: 5% = 500, 1% = 100, 22% = 2200, 15% = 1500. Rounding is always half away
from zero.

### Alternatives Considered

`decimal` in C# and `numeric` in the database. Exact on the backend, but JSON would have to carry
strings and TS would gain a second number representation. Integer kopecks are equally safe in
C#, PostgreSQL and JS.

### Consequences

Formatting into hryvnia happens only in the UI. User input is parsed into kopecks at the
boundary.

---

## ADR-004. Calendar dates by Europe/Kyiv, computed at the boundary

Date: 2026-09-25

Status: Proposed

### Context

Banks return UTC. The tax period is determined by the calendar date in Kyiv.

### Decision

A transaction stores `ValueDate` of type `date` (`DateOnly`, the Kyiv-time date) and optionally
`BankTime` as `timestamptz`. Converting UTC to a Kyiv date is done by the import adapter via
`TimeZoneInfo` `Europe/Kiev`. The engine works only with `ValueDate`.

### Consequences

The engine knows nothing about time zones. Changing the date rule touches a single adapter.

---

## ADR-005. Authentication: ASP.NET Core Identity, Google OAuth and passkey, email allowlist

Date: 2026-09-25

Status: Proposed

### Context

Single user, financial data, self-hosted, zero budget. The owner wants Google and passkey.

### Decision

ASP.NET Core Identity on schema version 3 (passkey is built into .NET 10), Google as the external
provider (`Microsoft.AspNetCore.Authentication.Google`, a free OAuth client in Google Cloud),
cookie session. Sign-in is allowed only for the email in `Auth__AllowedEmails`. First sign-in is
via Google; afterward the user registers a passkey as a second sign-in method. Everything is
free, with no external service.

### Alternatives Considered

Clerk, Auth0, Supabase Auth. Free tiers exist, but they are an unnecessary external dependency
for financial data. Self-hosted Keycloak or Zitadel. 300–500 MB RAM for a single user. Password +
TOTP. Requires storing hashes and recovery codes, an unneeded responsibility.

### Consequences

Opening the product to other FOPs comes down to removing the allowlist. The frontend makes
WebAuthn calls through the standard `navigator.credentials`, getting its options from the API.

---

## ADR-006. Deploy via Coolify on the existing VPS, reusing the existing PostgreSQL instance

Date: 2026-09-25

Status: Proposed

### Context

Coolify with Traefik, automatic certificates and several PostgreSQL instances already run on the
`blonskyi-dev` VPS. The owner also runs a general-purpose PostgreSQL instance on that VPS outside
Coolify's per-project resources. Budget: free.

### Decision

The application as a Coolify Docker Compose resource from the repository (services `web` and
`api`). Instead of provisioning a new Coolify PostgreSQL resource, a dedicated role and database
for this project are created in the PostgreSQL instance the owner already runs on the VPS. Cron
runs inside `api` as hosted services.

### Alternatives Considered

A new Coolify PostgreSQL resource per project. Simplest to wire up, but adds another PostgreSQL
process on a VPS that already runs one the owner maintains — unnecessary duplication for a
single-user app. Docker Compose by hand with Caddy. Duplicates what Coolify already does. Fly.io
or Railway. Paid at this memory footprint and add an external dependency.

### Consequences

Zero cost and no new database process. The application depends on Coolify for TLS and on however
the owner already backs up that PostgreSQL instance; if that instance has no backup in place, the
project adds its own scheduled logical dump.

---

## ADR-007. Year parameters in a table with a verification flag

Date: 2026-09-25

Status: Proposed

### Context

The minimum wage, rates, limit and deadline rules change every year. They cannot be hardcoded.

### Decision

A `tax_year_config` table, one row per year, holding every rate and deadline rule. Fields
`source` (a reference to the law or DPS letter) and `verifiedAt`. The application shows a warning
if the current year has no row, or it is not yet verified. A new year is created by copying the
previous one and editing the values through the settings UI. The initial 2026 values load via a
migration.

### Consequences

Rules update without a code change. The engine receives the config as an input parameter and
knows nothing about years.
