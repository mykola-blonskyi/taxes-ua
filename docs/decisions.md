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

Direct Google until the `login.blonskyi.dev` broker ships, then an OIDC client of that broker.
The broker is designed as the sign-in for every `*.blonskyi.dev` project and is not built yet, so
the Google handler here is the current step, not the permanent one.

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

That instance is Coolify's `shared-database`, and its other projects (`fitness`, `todo`, `hub`,
`plane`, `login`) share one convention: a login role `<project>_app` with no other attributes owns
the database `<project>`, with default privileges and no `pg_hba.conf` lines of its own. taxes-ua
follows it: `taxes_ua_app` owns `taxes_ua`.

### Alternatives Considered

A new Coolify PostgreSQL resource per project. Simplest to wire up, but adds another PostgreSQL
process on a VPS that already runs one the owner maintains — unnecessary duplication for a
single-user app. Docker Compose by hand with Caddy. Duplicates what Coolify already does. Fly.io
or Railway. Paid at this memory footprint and add an external dependency.

A role that does not own its database, with `pg_hba.conf` lines refusing it every other database.
Stricter, but it would be the only project on the instance set up differently, and it edits a
`pg_hba.conf` every other project depends on.

### Consequences

Zero cost and no new database process. The application depends on Coolify for TLS and for
backups. The instance had none, so Coolify's daily instance-wide dump to local storage and the
owner's MinIO was set up for it on 2026-09-28 (docs/deploy.md step 8). It covers every project on
the instance, not just this one.

The role can drop its own database and create schemas in it. Like every other project's role, it
can connect to the other databases on the instance, and they to this one, but no role can read
another's tables: each owns its objects and grants nothing on them.

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

---

## ADR-008. Backend organized as vertical feature slices, not layered by type

Date: 2026-09-25

Status: Proposed

### Context

`TaxesUa.Api` hosts Minimal API endpoints for roughly ten resources (auth, settings, tax years,
transactions, fx, payments, periods, dashboard, export, backup, audit). ASP.NET Core Minimal APIs
have no built-in `Controllers`/`Models` convention — that split belongs to MVC, which ADR-001
already ruled out. Microsoft's own guidance for organizing Minimal APIs at scale is one static
class per resource with a single `Map<Name>Api(this IEndpointRouteBuilder group)` extension
method, called once from `Program.cs`; that shape is already a vertical slice, not a layer. The
frontend is already organized the same way, as `features/<name>/{components,hooks,tests,
index.ts}` (see "web layers" in `docs/architecture.md`). The owner writes both sides by hand and
wants one mental model, and the MVP tickets (#4–#18) are already scoped one resource per ticket.

### Decision

`TaxesUa.Api` organizes by feature, not by technical type: one folder per resource under
`Features/<Name>/`, holding that feature's EF entity (or entities), its DTOs or inline records,
and its `<Name>Endpoints.cs` static class exposing a single `Map<Name>Api` extension method
called once from `Program.cs`. `Data/AppDbContext.cs` only aggregates `DbSet<T>` references; it
owns no business logic. Entities and feature-internal helper types are declared `internal`, so
only a feature's public endpoint-mapping method is visible to `Program.cs` and to other
features — the C# access modifier enforces the same boundary the frontend gets from eslint's
`no-restricted-imports`, with no extra tooling. `tests/TaxesUa.Api.Tests` mirrors the same
`Features/<Name>/` layout, one test file group per feature.

`TaxesUa.Engine` (ADR-002) is untouched by this decision. It stays a separate project and is the
one place pure business logic lives; feature endpoints reference it, they don't duplicate it.

### Alternatives Considered

A classic layered structure (`Controllers/`, `Models/`, `Services/`, `Dtos/`). Familiar from MVC
and from the default `dotnet new webapi` template, but that template defaults to it because it
assumes MVC controllers, which this project doesn't use. Splitting ten resources by type scatters
each feature's entity, DTO and route handler into three distant folders, and every ticket — each
already scoped to one resource — would touch all three on every change.

Clean/Onion architecture with separate `Application`/`Infrastructure` projects. Stronger
isolation, but adds project count and indirection with no payoff at this size. `TaxesUa.Engine`
already isolates the one boundary that matters here (business rules vs. everything else); a
second boundary inside the thin persistence-and-transport layer buys nothing for a single
developer and ten resources.

### Consequences

One folder maps to one ticket and to the same mental model already used on the frontend.
`AppDbContext` is the only file that touches every feature, by necessity of EF Core requiring one
`DbContext`; that coupling is deliberate and documented, not hidden. If the product later opens up
to multiple developers or the API grows well past today's resource count, revisit: vertical
slices scale per developer up to a point, and Clean Architecture's extra ceremony starts paying
for itself past it. Not a concern at the current size.

---

## ADR-009. No server-side session revocation

Date: 2026-09-26

Status: Accepted

### Context

The api signs the owner in with `AddIdentityCore`, `AddIdentityCookies` and `AddSignInManager`,
and registers no `ITicketStore`. The session cookie is therefore a self-contained encrypted
ticket: the server keeps no record of it and authenticates a request by decrypting the cookie it
carries. `POST /api/auth/logout` deletes the cookie from the browser and does nothing else, so a
cookie copied off the machine beforehand keeps working until its own expiry, and there is no way
to end every session from the server. Identity's security-stamp validator, which invalidates
tickets after a credential change, is not on this path either. It comes with the full
`AddIdentity` wiring, and `AddIdentityCore` does not add it.

### Decision

Session revocation stays absent. No ticket store, no session table, no security-stamp validation.

The application has one user. The cookie is `HttpOnly; Secure; SameSite=Lax` and is read only by
the api behind Traefik, so replaying it takes access to the owner's browser or machine, and that
access already carries the owner's Google session. Sign-out-everywhere has no second device to
serve and no second account to protect.

### Alternatives Considered

An `ITicketStore` over PostgreSQL or a distributed cache. Real revocation, paid for with a store
read on every authenticated request, a table to expire, and a new failure mode where the store is
unreachable and nobody can sign in.

`AddIdentity` with security-stamp validation. Revalidates the ticket against the user's security
stamp on an interval, which ends other sessions within that window. Cheaper than a ticket store,
but it revokes only after a deliberate stamp change, which nothing in the application performs.

A shorter cookie lifetime. Narrows the replay window with no machinery, at the cost of signing the
owner in again on a schedule. The cookie is persistent on purpose, because the owner works from
one machine.

### Consequences

A captured cookie replays until it expires. The only answer to a suspected theft is to rotate the
data-protection keys, which invalidates every ticket at once.

The key ring is persisted to a volume (ADR-010), so a redeploy no longer invalidates tickets.
Rotating the keys is now a deliberate step, and `docs/deploy.md` says how.

Revisit when a second account appears, when the api runs as more than one instance, or when the
owner wants to end a session from the interface. A ticket store is the smaller of the two changes
and the one to reach for then.

---

## ADR-010. Persist the data-protection key ring to a volume

Date: 2026-09-27

Status: Accepted

### Context

The session cookie, the external Google sign-in cookie and the passkey ceremony cookie are all
encrypted with ASP.NET Core's data-protection key ring (ADR-009). With nothing configured, the
key ring lives in the container's filesystem and a new container generates a new one. Every
redeploy then invalidates every cookie and signs the owner out, and a sign-in or passkey ceremony
in flight during a deploy fails.

### Decision

`Program.cs` persists the key ring to the directory in `DataProtection:KeysPath` when it is set,
with the application name fixed to `taxes-ua` so the keys do not depend on the container's content
root. `docker-compose.yml` sets it to `/var/lib/taxes-ua/keys` and mounts the named volume
`dataprotection-keys` there. The image creates that directory owned by the non-root app user, and
a fresh volume inherits that ownership.

The keys are stored unencrypted at rest, and the api logs a warning to that effect at startup.
Anyone who can read the volume can forge a session, but reading it takes root on the VPS, which
already reaches the database directly.

### Alternatives Considered

Accept a sign-in per deploy. No moving parts, but a deploy interrupts the owner, and the passkey
and Google ceremonies in flight fail with an opaque error.

Keys in PostgreSQL through `PersistKeysToDbContext`. Survives the api volume being lost, but adds
a table and a migration and ties the key ring to the shared instance and its backups, which would
then carry the means to forge a session.

`ProtectKeysWithCertificate`. Encrypts the keys at rest, but the certificate needs a home that
the volume is not, which only moves the secret.

### Consequences

A redeploy keeps the owner signed in. Deleting the volume, or the `key-*.xml` files in it, and
restarting the api rotates the keys and ends every session at once, which is ADR-009's answer to
a stolen cookie. `deploy/check-compose.sh` fails CI if the mount and the configured path drift
apart. A local Docker run persists its keys in the same volume under its own project.

---

## ADR-011. Encrypt the monobank token with a standalone key, not the Data Protection ring

Date: 2026-09-28

Status: Accepted

### Context

#75 stores the owner's monobank personal API token so the api can call the bank on the owner's
behalf. The token has to be encrypted at rest, and the api already has an encryption mechanism
wired up: the ASP.NET Core Data Protection key ring that protects session, external sign-in and
passkey ceremony cookies (ADR-009, ADR-010).

Reusing it would be the smaller change: no new key to generate, distribute or rotate. But ADR-009's
whole point is that a redeploy is the *only* thing that keeps that ring alive across restarts, and
deleting the ring's volume and restarting is the documented way to end every session after a stolen
cookie. A token protected by the same ring would be destroyed by that same action, silently
disconnecting monobank the next time the owner deliberately signs everyone out — a side effect
neither ADR mentions and nothing in that flow should have to account for.

### Decision

The token is encrypted with AES-256-GCM (`Features/Monobank/TokenEncryptor.cs`) under a dedicated
32-byte key, read once from `Monobank:TokenEncryptionKeyBase64`
(`MONOBANK_TOKEN_ENCRYPTION_KEY`), independent of the Data Protection ring. Each token is stored as
a fresh random 12-byte nonce, the ciphertext and a 16-byte GCM tag, concatenated in one `bytea`
column (`MonobankConnection.EncryptedToken`). The token never appears in a response, a log line or
the change log: `MonobankConnection` is not one of `AuditSaveChangesInterceptor`'s audited types,
and the token is write-only through the API (`MonobankEndpoints.cs` never serializes it back).

The key can be absent. `TokenEncryptor.IsConfigured` is false without it, and the api still starts
— unlike the required-outside-Development variables in `Program.cs`, because a fresh install with
no interest in bank sync should not be blocked from starting for a key it does not need yet. Every
monobank endpoint checks `IsConfigured` first and answers `503` with a
`monobank-not-configured` problem type when it is not, the same shape `/api/auth/login/google`
already uses for an unconfigured Google client. A key that *is* set but fails to decode, or does
not decode to exactly 32 bytes, still fails startup: that is a real misconfiguration, not an
intentionally-skipped feature.

`docker-compose.yml` passes `MONOBANK_TOKEN_ENCRYPTION_KEY` through from the environment like every
other secret. `docker-compose.local.yml` sets a fixed 32-byte base64 key so a local stack can
exercise the connected state without the owner generating one, exactly as its database credentials
are also fixed and local-only.

### Alternatives Considered

The Data Protection ring (`IDataProtector` with a distinct purpose string). Free encryption with no
new key to manage, but ties the token's lifetime to a ring that ADR-009 rotates on purpose to end
sessions, which must not also delete bank access nobody asked to revoke.

A key in the database (a `KeyEncryptionKey` table, itself protected by a passphrase). Survives a
Data Protection rotation, but moves the problem rather than solving it: the passphrase still needs
a home outside the database, which is exactly what an environment variable already is, with one
less table and one less migration.

### Consequences

Rotating the monobank key (a real key rotation, not a session-revocation rotation) requires
re-encrypting the stored token or asking the owner to reconnect; there is no way to do it in place
because the key lives only in the environment. With one owner and a token the owner can always
replace from monobank, asking for a reconnect is an acceptable cost.  The Coolify deploy generates
the key once during the runbook's setup step (`docs/deploy.md`) and treats it exactly like the
Google client secret: set once, rotated by hand when needed.


## ADR-012. The monobank webhook is a signal to sync, found by a secret path

Date: 2026-09-29

Status: Accepted

### Context

#79 wants a new operation to appear within a minute or two. monobank can call a URL the app sets
with `POST /personal/webhook`: it checks the URL once with a GET that must answer 200, then POSTs
`{type: "StatementItem", data: {account, statementItem}}` for each new operation, retries after 60
and 600 seconds when the answer is not a 200 within 5 seconds, and then disables the webhook.

The published OpenAPI describes no signature, no shared secret and no source address for these
requests. Anyone who learns the URL can POST any operation in the bank's shape, and the app cannot
tell it from the bank's.

### Decision

The webhook is a signal, never a data source. `POST /api/monobank/webhook/{secret}` answers 200 at
once without reading its body and queues a sync of that owner's followed accounts; the sync reads the
statement with the owner's token, and only the statement's rows are recorded. A forged body can at
most cause a statement read the rate gate already paces, and the sync queue keeps at most one waiting
copy per account however many POSTs arrive.

The URL carries a per-owner secret in its path: 32 random bytes as 64 hex characters, drawn on every
token save, stored on `MonobankConnection` and unique. The GET and the POST find the owner by it and
answer 404 for any other value, so the URL both routes the notification and keeps strangers from
queuing syncs. A token replacement draws a new secret, so a URL an earlier token registered stops
answering.

The app registers the webhook only when `Monobank:PublicBaseUrl` (`MONOBANK_PUBLIC_BASE_URL`) is set,
because the bank has to reach it; a local stack leaves it empty and never calls the webhook method.
Emptying it on a deployment that had registered webhooks removes them at the bank on the next start.
Registration runs in `MonobankWebhooks`, a hosted service with its own queue, on the rate gate's
`webhook` slot, after the token is saved, so a slow or failed registration never blocks the
connection and is shown in settings instead. Disconnecting removes the webhook at the bank with an
empty URL. On start the service registers again every connection whose stored URL differs from the
wanted one, and the nightly run (03:00 in Kyiv, `MonobankNightlySync`) sets every webhook again,
since the bank may have disabled one after three failed deliveries.

### Alternatives Considered

Trusting the body, which would save one statement call per operation. Without a signature that lets
anyone who learns the URL insert income, the one number the owner files with the tax office.

A fixed path authenticated some other way. monobank sends no header to check and publishes no
address range to allow, so the path is the only secret the request can carry.

Reading the body only to pick the account to sync. It narrows a sync from all followed accounts to
one, but the account id in the body is as forgeable as the rest, and a sync of three accounts costs
three paced calls.

### Consequences

A new operation arrives one statement call after the notification, so within a minute unless the
gate is busy with a backfill. Leaking the secret lets a stranger queue statement reads, which the
queue and the gate bound; it never lets them read or write data. The secret appears wherever a
proxy logs request paths, so replacing the token is the way to rotate it. A webhook the bank
disabled costs at most a day's delay, because the nightly run re-reads the last 31 days and sets the
webhook again.

---

## ADR-013. Freeze an invoice at issue and render one bilingual PDF from the frozen copy

Date: 2026-09-30

Status: Accepted

### Context

#92 issues invoices to foreign clients. An invoice is a primary document (Rule 14): what the owner sent
must be what the owner keeps, yet the seller's details, the payment details and the client's address
all live in records the owner edits later. Primary documents must be in Ukrainian or carry an authentic
translation, while the client reads English.

### Decision

Issuing is one operation under the owner's advisory lock: check completeness, take the next number of
the issue date's year, copy the seller, the buyer, the payment details of the invoice's currency, the
six clause texts and the signature image onto the invoice (`Snapshot` as jsonb, the image as bytes),
and set `Issued`. The PDF of an issued or cancelled invoice is rendered on every request from that copy
and the invoice's own lines, never from the live records; a draft renders the same layout from the live
records with a DRAFT mark, through the same function that builds the copy, so the preview shows what
issuing would keep.

The PDF is one A4 document with both languages side by side: each label is "English / Українська",
each line has both descriptions, the clauses sit in two columns, and the payment reference names the
invoice number in both languages. It uses the existing MigraDoc setup and embedded Noto Sans. A
signature image PDFsharp cannot read falls back to the seller's name, which is the identifying data the
invoice needs anyway.

### Alternatives Considered

Storing the rendered PDF at issue. It freezes the bytes rather than the facts, needs file storage and
a backup of binaries, and a font or layout fix could never reach old invoices. The facts are small and
render the same document.

Versioning the invoicing details and the client and pointing the invoice at a version. It spreads the
freeze across three tables and every edit screen for one reader.

Two PDFs, one per language. The Ukrainian translation must accompany the document, and two files invite
sending one without the other.

### Consequences

An issued invoice survives any later edit and a rename of the client. A restore refuses a file that
lacks the number of an issued or cancelled invoice the owner holds, so a number sent to a client is
never reused; drafts may be dropped. A layout change does
reach old invoices when they are downloaded again, so the layout must only ever add or reposition, never
drop a requisite. Paid and overdue (#93) read the invoice's own total and currency, never the snapshot.
The country name is frozen in English as ICU spelled it at issue.

## ADR-014. The app prepares payments and never initiates them

Date: 2026-09-30

Status: Accepted

### Context

#99 shows the owner where and how much to pay for each obligation. The app already knows the amount, the
recipient (Treasury accounts, Rule 12) and the purpose, so it could in principle send the transfer too.

### Decision

The app prepares payment details and never initiates a payment. Each obligation gets a Pay panel with the
recipient IBAN, name and code, the amount and the purpose, every field with a copy button. The owner
makes the transfer in the bank and confirms it there; the confirmed operation reaches the app through the
bank sync (#80). The QR (#100) prepares the same details and does not change this.

### Alternatives Considered

Initiating the transfer from the app. The monobank personal API is read-only, so it would need another
bank API with write access and signing keys, which the app would have to hold. A bug or a leaked key would
then move money, and the app would carry liability for it.

Showing the amount only. The owner would retype the recipient and the purpose each quarter, and a wrong
purpose or code sends the money to the wrong ledger.

### Consequences

The app holds no key that can move money, so it can never pay by itself. A wrong amount the owner confirms in the bank is still a real transfer, so the panel shows exactly what is owed and lets the owner check it first.
The owner takes one step in the bank for every payment. An incomplete recipient is not shown at all, only
what is missing, so a partial recipient is never copied.
The QR is offered alongside the copy buttons, never instead of them. Whether banking apps accept an NBU
QR for a Treasury account, which ISO 20022 category/purpose code they expect, and whether they read the
leading `101` as the payment type are unverified until the owner scans one (#100, Rule 16). The copy
buttons are the path that works regardless, and one flag in the panel hides the QR for Treasury accounts
if the scan shows banks refuse it.

### Amendment, 2026-10-02: a Treasury account can end (#173)

The panel shows the account in use, and the bank sync learns it from a payment, so nothing told the app that an
account stops working. The military-levy accounts used since 2026-07-01 are temporary: they are set for 1 July
to 31 December 2026 (Law 4908-IX), and the Q4 2026 levy is paid by 2027-02-19. The audit of 2026-10-02 found
the panel would offer the old account for that payment.

Each stored account (the Manual one and the Learned one) gets an optional `ValidUntil`, the last day it can
receive a payment, and belongs to its IBAN. The panel is used to pay now, so the details endpoint judges the
account on today in Kyiv: after the end it answers `expiry: Expired` with no recipient and no QR, so the panel
can only point to settings; on or before the end, with the due date after it, it answers `ExpiresBeforeDue` and
still gives the details with a note. Judging on the due date was tried first and rejected: it hid a working
account in December from an owner who meant to pay then. The end is never guessed: a learned account has none
until the owner sets one, because the app cannot tell a temporary account from a permanent one, and what applies
from 2027-01-01 is not published. Rejected: hiding a levy account by a hardcoded year, which would break the day
the Treasury publishes a longer-lived account, and warning only, which still lets the owner copy a closed
account. The cost is that the owner must
set the end once; settings offers 2026-12-31 for the levy in one tap. A backup carries both ends (schema 17).

---

## ADR-015. Read Telegram by long polling, not a webhook

Date: 2026-09-30

Status: Accepted

### Context

#106 connects a Telegram chat so reminders can reach the owner. The bot has to notice the owner pressing
Start on the deep link `https://t.me/<bot>?start=<code>`, so it must receive updates. The Bot API offers
two ways: `setWebhook`, where Telegram POSTs each update to a public HTTPS URL, and `getUpdates`, where
the app asks for them and Telegram holds the request open until an update arrives or a timeout passes
(long polling). The two are exclusive: `getUpdates` fails while a webhook is set.

### Decision

Long polling. A hosted service calls `getUpdates` with a 30 second timeout for as long as a token is
configured, allowing only `message` updates, and does nothing without one. The offset is stored in
`TelegramPollState`, keyed by the bot's id, and moves in the same save as the effect of the update it
passes: a restart resumes exactly after the last handled update. A failed round waits 5 seconds,
doubling to a minute. Only `/start <code>` from a private chat acts; any other private message gets one
short reply in the sender's language.

The link code is 24 random bytes, stored as a SHA-256 hash, valid for 15 minutes, redeemable once, and
replaced by asking for a new one.

### Alternatives Considered

A webhook. It needs a public route, a secret to authenticate Telegram's calls (a `secret_token` header),
and a `setWebhook` call at every deployment, and the route must be reachable through Traefik and the
web proxy. That is a new anonymous endpoint on an app whose public surface is otherwise the login page and
the secret-path monobank webhook (ADR-012), to save a request that costs nothing when idle. It also makes a local run unable to receive
anything without a tunnel. Polling needs only an outbound HTTPS call, which the VPS and a laptop both
have.

### Consequences

Nothing new is exposed to the internet. One process may poll a given bot: a second instance, such as a
local stack started with the production token, makes Telegram answer 409 to one of them, so the local
stack must use its own bot or none. A 409 also means a webhook is set on the bot, and then linking would silently never complete; on the first 409 the poller
calls `deleteWebhook` and logs it (again on a later 409 only if that failed), and a 409 that is another poller's is unaffected by that call. The api already runs as a single instance for the same reason as the
monobank queue. Links complete within about a second while the service is up and wait, without loss,
while it is down, because Telegram keeps updates for 24 hours. Idle cost is one open request. The token
sits in the request path of every call, which is why the client is registered without the framework's
request logging and never logs a URL or an exception message.

---

## ADR-016. The app prepares the declaration file; the owner signs and sends it in the Cabinet

Date: 2026-09-30

Status: Accepted

### Context

#111 turns the quarter's declaration figures into the F0103309 XML the Electronic Cabinet imports. Filing
needs a qualified electronic signature (KEP) and a session in the Cabinet. The format is fixed by the
DPS: the published XSDs, windows-1251, and the file name standard No. 729. The official register of
forms (tax.gov.ua, reestr-form) refuses automated fetches, so the schemas cannot be pulled at build or
run time.

### Decision

The app prepares the file and stops there. The owner checks it against the Declaration screen, signs it
with a KEP and sends it. (ADR-025 assumed the Cabinet had no XML import; its amendment of 2026-10-03
restores the import as the main path.) The app holds no
key, no Cabinet session and no DPS credential.

F0103309.xsd and common_types.xsd are vendored next to the writer, byte for byte, with their source,
commit, fetch date and hashes in a README, and embedded in the api. Every file is validated against them
before it is stored or downloaded; a file that fails is never handed out, and the owner sees the
validator's errors instead. The writer is a pure function of the declaration figures, the details and
the fill date, so golden files pin its output byte for byte. The last file per quarter and type is
stored with its generation time and travels in the backup as the record of what was prepared.

The file is built only for a quarter whose last day has passed in Kyiv (#163, Rule 15). The rule is
pure and lives in the engine (`Declaration.FileAvailable`, given `today`); the api takes today from
`TimeProvider` and answers 409 with the closed reason `QuarterNotEnded` and `availableFrom`. A file
generated before the quarter ended is stale: left out of the quarter's file list and refused on
download with 409 `GeneratedBeforeQuarterEnded`, even after the quarter ends, but kept in the table and
the backup.

### Alternatives Considered

Signing and sending from the app through the DPS gateway. It needs the owner's key on the server and a
certified integration, for a filing that happens four times a year and takes a minute in the Cabinet.

Fetching the schemas at run time. The register refuses scripted fetches, and a schema that changes under
a running app would break downloads without a code change to review.

Not validating before download and relying on the Cabinet's import check. The owner would learn about a
bad file only in the Cabinet, with the filing deadline close.

### Consequences

A new form version means replacing the vendored schemas and the writer together, with new golden files;
the README says how. The vendored copies come from a mirror, so the owner confirms once by hand that they
match the register before the file is relied on. Import into the Cabinet is proved by hand, not by
tests. Stored files do not follow later edits: preparing the file again replaces it.

---

## ADR-017. The calendar feed is a secret path that serves deadlines, never amounts

Date: 2026-09-30

Status: Accepted

### Context

#105 lets the owner subscribe a phone or desktop calendar to the deadlines. A calendar app fetches a
URL on a timer and sends no cookie, no header and no token, so the subscription cannot sit behind the
session. Whoever learns the URL can read what it serves.

### Decision

The feed is an anonymous `GET /api/calendar/feed/{secret}.ics`, found the way the monobank webhook is
(ADR-012): a per-owner secret of 32 random bytes as 64 hex characters, in the path, unique. The row is
`CalendarFeed`, one per owner, created the first time the owner asks for a link and replaced whenever
they rotate it. An unknown, malformed or rotated secret answers 404. The endpoint is left out of the
OpenAPI document. The secret is stored as drawn, not hashed, because settings shows the URL again;
a 256-bit value makes the lookup by index safe against guessing, and a hash would only make the
owner rotate every time they want to copy it.

The document carries no amount, only the kind, the period and the date, so a leaked link reveals when
the owner pays and files and nothing about what. A request line prints its path, and this one is a
secret, so the framework's request logging (`Microsoft.AspNetCore.Hosting.Diagnostics`) is held at
Warning in every environment; Development raised it to Information, which is also what the local stack
runs. The secret is not audited and not in the backup: a restore on a new server creates no feed and
leaves an existing one alone, and the owner creates or rotates one in settings. `Cache-Control:
no-store` keeps an intermediary from holding a copy.

The document is an RFC 5545 VCALENDAR of all-day VEVENTs with a stable UID per deadline
and owner (`esv-2026-q1-3f9a1c2e@taxes-ua`, `advance-2026-m03-3f9a1c2e@taxes-ua`, the suffix being the
first 8 hex characters of SHA-256 of the user id, not secret), so a client updates an event when a date
moves instead of adding a second one, the UID survives rotation and re-subscription, and two owners or a
feed beside an import never share one. The feed lists every deadline dated this calendar year or next,
which includes last year's Q4 and December advance until January and February have passed them. Each
event has two DISPLAY alarms, `-P6DT15H` and `-PT15H`: an all-day event starts at 00:00, so they fire at
09:00 local, 7 and 1 days before, when the Telegram reminders go out. The owner's settings locale chooses Ukrainian or Russian for the
summaries. The same document is served to the signed-in owner at `GET /api/calendar/deadlines.ics`
as a download, with or without a subscription.

### Alternatives Considered

A token in a header or query of an authenticated route. Calendar apps send neither.

Hashing the secret. It would make the URL unrecoverable, so settings could show it only once.

A fixed URL per owner derived from their id. It could not be revoked.

### Consequences

Anyone with the link sees the owner's deadlines until the owner rotates. Rotating breaks existing
subscriptions by design; the owner subscribes again. The path appears wherever a proxy in front of the
app logs request paths, which this app does not control. Alarms are a calendar-side nudge at 09:00
local; the reminders of #108 remain the message that names what is owed.

---

## ADR-018. Annex 1 travels with the year's last group 3 declaration, as a second file stored beside it

Date: 2026-09-30

Status: Accepted

(ADR-025 assumed the Cabinet had no XML import. Its amendment of 2026-10-03 corrects that: the owner imports the two files in the Cabinet with "Завантажити", as below.)

### Context

#112 adds annex 1 to the declaration (form F0133109): the ESV for oneself, month by month, with its base,
rate and amount, whose total is the declaration's line 21. Three things were open. The mirror that
supplied F0103309.xsd has no F0133109.xsd. Rule 15 left open whether, after a limit crossing in Q1 to Q3,
the year's group 3 ESV goes on the crossing quarter's declaration, since the app filled line 21 only in
Q4. And the stored declaration file had room for one file.

### Decision

The annex goes with the year's last group 3 declaration: Q4, or the crossing quarter's after a crossing,
marked H03 ("перехід на сплату інших податків і зборів"). The form itself provides for this: its footnote
9 describes the FOP who moved to other taxes and asks for item 8, the stretch on the simplified system.
The engine builds the annex once, in the year's accruals, and the declaration's line 21 reads the
annex's total, so the two cannot differ.

The engine prorates the registration month's ESV base, not its amount, and applies the rate to the base,
so each annex row satisfies column 4 = column 2 × column 3. The registration month's ESV is now base ×
rate, which can differ by 1 kopeck from the old figure (the month's ESV prorated) on some dates: with the
2026 minimum wage, registration on 5 April was 1,648.69 UAH and is now 1,648.70.

The annex is a second file of the same filing, not a filing of its own: `DeclarationFile` gains
`AnnexFileName` and `AnnexContent`, both set or both null by a check constraint, and the backup carries
them (schema version 13). The two files are written together because each names the other in
LINKED_DOCS, validated each against its own schema, stored together and downloaded from the same row.

F0133109.xsd comes from a second mirror, https://github.com/lzeal/tax-fop-3rd. Its F0103309.xsd is byte
for byte the copy vendored from the first mirror, which is the evidence that it carries the DPS files
unaltered. The README records the source, commit, date and hash, and the owner confirms it against the
register by hand, as for the others.

### Alternatives Considered

Keeping the annex on Q4 only. After a Q1 to Q3 crossing there is no group 3 Q4 declaration, so the group 3
months' ESV would be declared nowhere.

A second `DeclarationFile` row per form, keyed by form. It lets a declaration exist without its annex, or
an annex without the declaration it links to, and every reader would have to pair them up again.

One zip with both files. The Cabinet imports XML files, so the owner would unpack it first.

### Consequences

The crossing quarter's declaration now shows line 21 and comes with the annex. A clarifying annex leaves
item 10 (the correction of the earlier annex's ESV) for the owner to fill in the Cabinet. The owner still
confirms the pair in the Cabinet by importing it without sending, the acceptance check of #112.

### Amendment, 2026-10-02: the registration month owes the full minimum (#171)

The proration above assumed the owner's decision of 2026-09-27, that the registration month's ESV
scales by active days. That decision rested on a wrong premise. Law 2464-VI sets the ESV of a FOP on the
simplified system at no less than the minimum insurance contribution (art. 7 part 1 item 3) and has no
part-month minimum, and the DPS says the full monthly minimum is due for the month of registration
(Rule 3 lists the sources). The audit of 2026-10-02 found the owner's Q3 2026 ESV shown as 190.23 UAH
instead of 1,902.34, with annex 1's September base and line 21 short by the same 1,712.11.

`FullMonth` is now the default. A migration moves every owner on `Prorated` to `FullMonth`, because
nobody chose `Prorated` against the law knowingly: it was the default or the confirmed reading. In the
same statement it writes, for each owner moved, the settings update the audit interceptor would have
logged, so the switch shows in the owner's change history. Its Down does nothing, since a row moved
cannot be told from one that was always `FullMonth`.

A backup file of schema version 15 or older cannot tell the old default from a deliberate choice, so a
restore of it reads `Prorated` as `FullMonth`, in the upgrade from version 15. Erring this way overstates
ESV, never understates it. Schema 16 (#172) is written only after this change, so a version 16 file
restores `Prorated` as it is: there it is the owner's choice.

`Prorated` stays as a setting, labelled in the interface as not matching the law. Removing it was not
cheap: the backup carries the field, and dropping a value or the field needs a schema version, and every
caller of the engine's settings input would change. The base × rate order and its 1-kopeck notes above
still hold for that path. Declaration files already stored keep their bytes; the next one prepared reads
the new figures.

---

## ADR-019. Compute reminders at each run and claim each one in a sent log before sending


Date: 2026-09-30

Status: Accepted

### Context

#108 sends deadline reminders (Rule 17). What a reminder says depends on what is owed when it goes out:
a payment recorded on Monday must drop Tuesday's reminder or shrink its amount. It has to reach each
channel once, across restarts, redeploys and a run that overlaps another, and a reminder whose moment
passed while the server was down should still go out if it can still help. A Telegram send cannot be
part of a database transaction, so a crash between sending and recording leaves one of the two undone.

### Decision

Reminders are computed, not scheduled. A pure planner in the engine, `ReminderPlan.Due`, takes the ledger
years, the Rule 7 allocation, the advances in advance mode, the filed marks and the Kyiv day and time,
and returns what is due now. A hosted worker runs it for every owner with an enabled channel every 5
minutes and at start.

Delivery is at most once. Before sending, the worker inserts a `SentReminder` row keyed by owner, date,
kinds, offset and channel, and commits it; the unique key makes a second, concurrent run fail that
insert and skip. The row gets `DeliveredAt` when the channel accepts the message. A failure that proves
nothing was delivered (unreachable, rate limited, server error) deletes the row so a later run retries.
Any other failure keeps it, and so does a timeout: the message may have arrived before the answer was
lost, so it is not retried, in the channel or by a later run. A crash after the insert leaves the row,
and the message is never sent again.

Channels are behind `IReminderChannel` (kind, availability, send). Telegram's wraps `TelegramDelivery`,
so its retries, its failure record and its 403 switch-off apply to reminders unchanged; email (#107) adds
one class.

### Alternatives Considered

Pre-scheduled reminder rows, which the domain model first sketched. Each payment, refund, settings change
or filed mark would have to find and rewrite the rows it affects, and a missed rewrite sends a wrong
amount. Computing costs one ledger load per owner per run, which the home screen already does per
request.

Send, then record (at least once). A crash or redeploy between the two, or a run cut off by shutdown
while Telegram holds the request, sends the same reminder again on restart. A duplicate tax reminder
teaches the owner to ignore them; a lost one is covered by the next moment for the same date (1 day
before, on the day) and by the home screen.

Recording each kind separately. The key would be simpler to reason about, but one message names several
kinds, and a partial record would either repeat the message or lose kinds from it. The log keeps the
message's kinds instead, and a run compares its kinds with the union already sent for the date, offset
and channel.

### Consequences

Where the link points. A reminder ends with a link to the home screen: `App:PublicUrl` (`APP_PUBLIC_URL`) when
set, otherwise `https://` and the first `ALLOWED_HOSTS` domain, which production already pins; with
neither, or with a wildcard host, the message goes without a link. A malformed `APP_PUBLIC_URL` fails
startup rather than every reminder.

The sent log is not in the backup and a restore does not touch it (the backup schema version is
unchanged), so restoring never makes an already-sent reminder go out again.

Accepted gap. The planner reads the ledger once per run, and the claim and the send follow. If a payment
lands within about a second of a run that has already read the old amount, the owner can get one message
with the old amount, and two runs racing within that second can send two messages for a changed set of
kinds. Closing it would need a lock around payment recording and reminder sending, which a one-second
window on a single-owner app does not justify.

The amount is always the current one, and a paid obligation is never reminded. Downtime costs nothing up
to the date: the latest passed moment goes out late with the real number of days left. A crash at the
wrong instant loses one message, never duplicates one. The sent log is not backed up (domain model,
`SentReminder`). The planner is a pure function with table tests, and the worker is tested with fake time
and the Telegram stub.

---

## ADR-020. Test the web in three layers: unit, component and end to end

Date: 2026-10-02

Status: Accepted. Supersedes the "no direct component tests" line in the MVP spec's Testing Decisions
(`plans/spec-mvp.md`).

### Context

The MVP spec verified the web by hand against each ticket's acceptance criteria, on the grounds that the
web was UI only. That no longer holds. The web parses the hryvnia amounts the owner types, encodes the NBU
payment QR, warns when a payment names a period outside group 3, reads the bank's `Retry-After`, explains
IBAN errors, runs the email confirmation token flow and suggests invoice payments. A wrong figure on screen
can cost real money. Every UI ticket is proved in a real browser, but each proof is a one-off that nobody
re-runs, and CI checked the web with lint, typecheck and build only. The group 3 hint bug of #145 was caught
by a reviewer, not by a check.

### Decision

Three layers run on every pull request, and the deploy waits for all of them (#149).

1. Unit tests (Vitest with jsdom, as the bundled Next.js 16 testing guide sets it up) call the web's pure
   logic through its exported functions with table-driven cases, edge cases included: amounts, dates, the NBU
   QR encoder, API error parsing, the IBAN message mapping, the group 3 hint. Tests sit next to the code
   they test, as `*.test.ts(x)`, and `pnpm test` in `web/` runs them in seconds. A hook that needs React is
   rendered once on the server with its query cache filled, which needs nothing beyond React and TanStack
   Query.
2. Component tests (#151) render the client components that carry state or branching, with the owner's real
   Ukrainian and Russian message catalogs, Testing Library and a typed fetch stub in place of the API. They
   assert what the owner sees and which requests are sent, finding elements by role and label. Async Server
   Components cannot be rendered by Vitest, so they are covered only by layer 3.
3. End-to-end tests (#152 to #154) drive Chromium through the core flows against the real stack in
   Development mode, with Telegram and monobank stubbed through their base-URL settings. The 375 px layout
   check in both languages becomes an automatic gate there.

A good test drives behaviour through the public surface and asserts what the owner observes. There are no
snapshots and no assertions on internal state or markup. The existing lint boundaries apply to tests, and
the test harness module (#151) is imported only from tests. From now on a UI ticket adds or updates
component tests for what it changes, and a new owner flow adds an end-to-end scenario.

### Alternatives Considered

Keeping manual verification only (the MVP spec). It is cheap per ticket, but the proof is not repeatable,
so a regression surfaces in review or in production.

End-to-end tests alone. They cover the whole path, but they are slow and few, so the arithmetic and parsing
edge cases would go untested, and a failure points at a flow rather than at the line that broke.

MSW for the component tests' API. A typed `fetch` stub keyed by method and path is enough at this size and
adds no dependency.

Coverage thresholds and visual regression snapshots. A threshold rewards tests that execute code without
asserting anything; a snapshot asserts on markup, which is what the tests are meant not to do.

### Consequences

Three dev dependencies for the runner (`vitest`, `jsdom`, `@vitejs/plugin-react`),
`@types/node` raised to 24 to match the Node the image and CI run and what Vitest asks for, and a `web-test` CI
job that the deploy job needs. Tests run in a time zone far from Kyiv, so a date that shifts with the zone fails here and not in production. A pure function that lived
inside a component file moves to a sibling module when a test needs it (the IBAN message mapping did).
The component and end-to-end layers add Testing Library, Playwright and a CI job each, in their own tickets.

The end-to-end layer (#152) adds `@playwright/test` (Chromium only), the suite in `web/e2e/` and an `e2e` CI
job that the deploy job needs. `pnpm e2e` builds the Compose stack in Development mode under its own project
name and a random port, and removes it with its volumes afterwards. Telegram and monobank point at in-process
stubs. The suite runs on one worker against one database, so each test seeds its own data and asserts on the
change it made rather than on absolute totals. A failed CI run uploads the report and traces.

#153 adds the core owner flows: the pay panel with its QR, the declaration XML, the invoice PDF, the
unavailable notification channels and the language switch. Each seeds its data through the API
(`web/e2e/support/seed.ts`); the invoice is created and issued through the API, and the UI create flow is
not driven. A download is checked by what identifies it, not by being present: the XML by its DPS file
name, its windows-1251 prolog, its parsed form code and its Cyrillic header values; the QR by its start
code and the fields of the decoded payload (the image is not decoded); the PDF by its header, trailer
and the number and seller in its information dictionary. The PDF's body text is not read, because that
needs a PDF library; it is covered by the API's own tests (`InvoicePdfTests`, PdfPig).
The stack starts with the bot token and SMTP settings blanked, whatever the developer's shell holds, and
the NBU base URL (`Nbu__BaseUrl`, passed through `docker-compose.local.yml` only, empty meaning the real
NBU) points at an in-test stub, so the suite cannot reach the real NBU. Its own data is in hryvnias.

---

## ADR-021. Read the reserve jar through the rate gate without ever waiting for it

Date: 2026-10-01

Status: Accepted

### Context

#102 compares a monobank jar with what the taxes need. The jars come only from `client-info`, which
monobank allows once a minute per token. The gate (`MonobankRateGate`) paces by making a caller wait for
its slot, which is right for the sync worker's statement calls but would park an HTTP request, or the
worker that serves every owner, for up to a minute here. Listing the jars, choosing one and refreshing its
balance would also each cost a call if they did not share an answer.

### Decision

`MonobankClientInfoReader` is the one way `client-info` is read, for the jars and for the invoicing
prefill's name: `MonobankClient.GetClientInfoAsync` behind the gate's `client-info` slot, taken with the
new `TryTakeTurn`, which succeeds only when the slot is free. It keeps its last whole answer (name and
jars) in memory per owner for one gate interval and serves it to any read inside it. A token
save, which validates the token with `client-info` outside the gate, marks the slot used whatever the
answer and, when the token is good, hands the reader its answer. The save is the one call that is not
gated: it must check the token now, so if a gated read took the slot in the minute before it, the bank
can see two calls within 60 seconds (a 429 to the save reads as the bank being unavailable and nothing is
stored). Gating it with `TryTakeTurn` and answering 429 would refuse a valid token for no reason of the
owner's, so the exception is accepted. The reader is a singleton that takes the client as an argument
from the caller's scope and numbers each owner's token: a read that began before a save or a disconnect
is discarded rather than cached. A read that finds the slot taken is skipped by a sync run, which refreshes the jar after
the statement and tries again at the next run, and is answered `429` with `Retry-After` for the owner's
refresh. Only the chosen jar is stored (`ReserveJar`: id, name, balance, the time the bank reported it), and
a balance that cannot be refreshed keeps its time, marked stale after 24 hours. The row is not audited: it
is rewritten by every sync and holds the owner's savings. It is in the backup (schema 14), and
disconnecting monobank leaves it.

### Alternatives Considered

Waiting for the slot with `WaitTurnAsync`, as the invoicing prefill once did (it now reads through the
reader and answers `429` when the slot is spent and no answer is held, like the jar refresh). It holds a request or the shared
worker for a minute whenever another call came just before, and the jar read follows every sync run.

Storing every jar from every `client-info`. The picker would need no bank call, but the owner's other
savings, names and balances would sit in the database and the backup for no use.

### Consequences

A refresh within a minute of another `client-info` call gets the earlier balance, or a `429` when the slot
was spent without an answer to keep: a refused token save, a bank that was unavailable, a token the bank
rejected, or a read that was discarded because the token changed or cancelled before it answered. The
screen shows the time of the balance either way. The invoicing prefill shares the same answer, so a prefill right after connecting monobank
needs no bank call. A balance is at most a sync run old plus whatever the slot skipped. The answer lives in process memory,
so it needs the single api instance the queue and the gate already need.

---

## ADR-022. Send email with MailKit, and confirm an address with a signed, expiring link opened in the owner's session

Date: 2026-10-01

Status: Accepted

### Context

#107 adds email as the second reminder channel. Three things were open: which library speaks SMTP, how the
owner proves an address is theirs, and how a channel that can hold an address nobody has confirmed fits the
channel model Telegram set (ADR-015, ADR-019).

### Decision

**Library: MailKit 4.18.1** (MIT, free, self-hosted, the only new package; it brings MimeKit). The built-in
`System.Net.Mail.SmtpClient` was the first choice, since the project prefers the framework to a package,
and it was ruled out on the facts rather than on taste. Its documentation says not to use it for new
development and recommends MailKit. It cannot do implicit TLS (port 465, the mode many providers
document first), only STARTTLS on 587, so a deployment could be unable to use its own provider. It does
not tell a refused sign-in from an unreachable server, a 4xx from a 5xx reply, or a failure before the message
was handed over from one after it, and ADR-019's rule depends on exactly that last distinction. MailKit
has typed exceptions for each (`AuthenticationException`, `SmtpCommandException` with its status code,
`SmtpProtocolException`), explicit `SecureSocketOptions`, and a timeout per socket operation. The cost is
one dependency, kept behind one interface, `IEmailTransport`, so replacing it touches one class.

**Settings.** `SMTP_HOST`, `SMTP_PORT`, `SMTP_TLS` (`starttls`, `implicit`, `none`), `SMTP_USER`,
`SMTP_PASSWORD`, `SMTP_FROM`, read once into `EmailSettings`. They live only in configuration, in Coolify
for production. Nothing logs them: the class has no `ToString` that prints the password, the transport logs the
failure class and the exception type and never a message (a server's reply can quote the address), and no
protocol logger is attached. The channel is available only when host and sender are valid, user and password
are both set or both absent, credentials are not combined with `none` unless the host is on this machine (a password never crosses a network in the clear),
and an address for the link exists (`APP_PUBLIC_URL`, else the first `ALLOWED_HOSTS` domain). Anything else
leaves it unavailable with one warning naming the variable, never failing startup, as for the Telegram token.

**Channel model.** One `NotificationChannel` row per owner and kind, as before, with a new `ConfirmedAt`.
Telegram sets it with `LinkedAt`. An email row exists from the moment the address is added, with
`ConfirmedAt` null and `Enabled` false, and the shared delivery code refuses to send anything but the
confirmation to it; the reminder sender also selects only confirmed rows, so no path reaches an unconfirmed
address by omission. The retries, the failure record and the 403-style switch-off moved out of
`TelegramDelivery` into `ChannelDelivery`, which both channels use, so the retry policy exists once.
`EmailReminderChannel` is the second `IReminderChannel`: `ReminderSender` is unchanged, and the channel is
already part of the sent log's key, so one reminder is claimed once per channel.

**Failure semantics.** The transport connects and signs in first, then sends, and classifies by phase. A
refusal to connect or a timeout before the message is handed over is `Unreachable`, retried (nothing was
delivered). A sign-in refusal is `Authentication` (new), final. A 4xx reply is `ServerError`, retried; a 5xx
is `Rejected`, final. A timeout or a lost connection after the hand-over is `Timeout`: the server may have
accepted the message before the answer was lost, so it is not retried, in the channel or by a later run, and
its claim is kept (ADR-019's rule, unchanged). The three retries after the first attempt, at 1, 2 and 4
seconds, apply to reminders only; a test message and a confirmation make one attempt.

**Confirmation link.** `https://<app>/settings?tab=notifications&confirmEmail=<token>`. The token is the
owner's id, the lower-cased address and the expiry (24 hours), protected with ASP.NET Core's data-protection
key ring under its own purpose string: authenticated and encrypted, so a changed character, another purpose
or another key ring fails to open. The expiry is checked against the app's `TimeProvider`, not the
protector's own clock, so it is testable. Nothing is stored: asking for a new link does not need to cancel the
old one, because the token names the address, and a link for an address since replaced or removed opens
nothing (410). The key ring is on a persistent volume (ADR-010), so links survive a redeploy.

The settings page spends the token by posting it to `POST /api/notifications/channels/email/confirm` under
the owner's own session, then removes it from the address bar. The confirm call needs the session and the
token's owner must be the signed-in owner (a link opened by another account is a 400).

### Alternatives Considered

`System.Net.Mail.SmtpClient`, above.

A confirmation on a plain `GET` that works for whoever holds the link, which is the usual double opt-in.
A mail scanner or link preview fetches the link before the owner sees it and would confirm an address the
owner never read, and nothing would say the person who opened it is the owner. Doing it in the app, in the
owner's session, costs one sign-in when the link is opened in a browser that is not signed in (the redirect to
sign-in does not carry the link back, so the owner opens it again) and nothing else, for an app with one owner.

A random code stored hashed, like the Telegram link code (ADR-015). It would work and is also simple, but it
needs a table or a column, a cleanup of expired rows and a rule for superseding earlier codes, and the ticket
asks for a link that is signed. The data-protection token needs none of that.

A separate `EmailChannel` table. It would keep `NotificationChannel` free of a null column, and duplicate the
toggle, test, remove and failure endpoints, the audit and the backup row for a second shape of the same idea.

An allowlist of addresses, or confirming only on the first reminder. The first does not prove the owner
can read the mailbox; the second sends a tax reminder to a typo.

### Consequences

One new package. Email depends on an SMTP server the owner supplies; deliverability (SPF, DKIM, the sender
domain) is the server's and the owner's, not this app's, and the deploy runbook says so. The test-email and
confirmation calls make one attempt under one overall deadline of 25 seconds (the socket timeout of 20 seconds
applies to each step, so it alone would not bound the attempt), under the web proxy's 30 second rewrite timeout; retries belong to reminders, where nobody is waiting, and the owner presses the
button again after a failure, which is shown on the channel. There is no cooldown on asking for the confirmation again: only the
signed-in owner can ask, and an address is the owner's own choice. Addresses are plain ASCII addresses
(no display name, no internationalised domain); a Cyrillic domain is refused with a clear message and can be
added if it is ever needed. A restored backup carries the address but no token and no delivery record (schema 15),
and an email address comes back unconfirmed and switched off whatever the file says: a file proves nothing
about a mailbox (it may be edited, or restored on another server), so the owner sends the link again. A Telegram
channel came back confirmed at first, since a chat id is only ever linked by pressing Start; #180 changed that: it
too comes back unconfirmed and switched off, because a tampered file could otherwise point reminders at any chat,
and pressing Start again is one tap. The backup upgrade runs 14 to 15 after main's 13 to 14; the migration adds the column and marks every existing
channel confirmed, since all of them are Telegram chats.

---

## ADR-023. Group 3 starts on its own date, and the app says when that date is unconfirmed

Date: 2026-10-02

Status: Accepted

### Context

The engine taxed everything from `FopRegistrationDate` as group 3. Registration does not make a FOP a
single tax payer. The DPS register does, after an application (Tax Code 298.1.2, 298.1.4). For this
owner the group 3 record never appeared after registration on 2026-09-28. Nothing in the app could say
so, and every figure was shown with the same confidence as a confirmed one (audit of 2026-10-02,
domain finding 1 and UX High 4, #172).

### Decision

**A start date apart from registration.** `Settings.Group3Since` is the registration date, or the
first day of a later quarter, the only two starts the Tax Code allows. The api refuses any other date,
so the engine never sees a group 3 start in the middle of a quarter after the registration date. Null
means the registration date, and the registration date itself is stored as null, so a corrected
registration date never leaves a stale start behind. The migration backfills nothing: every existing
owner has null, which is what the app assumed until now.

**The time before it is not computed, except ESV.** The engine's group 3 start is the later of the
two dates. A quarter that ends before it gets no single tax and no military levy, and income dated
before it is left out of group 3 income. This reuses the "outside group 3" path of the limit crossing
(Rule 4): single tax and levy payments naming such a quarter stay out of the ledger, and the
declaration skips it. ESV does not depend on the tax system, so such a quarter still accrues it from
the registration date. Those months are reported on the ESV annex of the general system's annual
property and income declaration (Tax Code 298.1.2, 298.1.4), not on the group 3 annex 1, so the group 3
annex and line 21 cover only the group 3 months, from the group 3 start. The app does not build the
general system's declaration. The engine
reports the stretch from registration to the start, with the income received in it, as one figure per
year rather than one warning per operation. The interface states it once.

**Confirmation is the owner's mark.** The app cannot read the DPS register, so confirmation is a date and
a receipt number the owner enters from the Cabinet. Until it is set, the dashboard shows a banner
linking to a manual checklist, and the declaration screen warns beside the file and the filed mark.

**Warn, do not block.** The audit proposed blocking "mark filed" and the declaration file until
confirmation. We warn instead. The owner can see the register; the app cannot. A block would stop a
legitimate filing whenever the owner forgets the mark.

**The 10 days are a tax year parameter.** `TaxYearConfig.Group3ApplicationDays` (10) holds the term of
298.1.2, like the other statutory terms. The registration year's value applies. The dashboard and the
reminder plan compute the deadline with one engine function, so they cannot disagree.

### Alternatives Considered

Modelling the general system for the stretch before group 3: 18% personal income tax and 5% levy on net
income, quarterly advances and an annual return. That is a second tax engine with expenses, a different
declaration and different deadlines. The app names the stretch and the income in it instead, and says
those taxes are owed.

Dropping ESV for that stretch too, as a quarter after a limit crossing does. ESV is owed from
registration whatever the system, so leaving it out would understate a real debt.

A free date for "group 3 since". It would allow a start in mid-quarter after registration, which the law
does not, and the engine would have to split a quarter between two systems.

Keeping the confirmation implicit: assume group 3 while the registration date is set, as before. That
is what failed for this owner.

### Consequences

The settings row gains the start date, the confirmation (date and receipt, both or neither) and three
checklist ticks. They are served by their own endpoint, so the FOP settings form, which writes the whole
settings request, cannot wipe them. The backup goes to schema 16. The reminder kinds gain
`Group3Application`. An owner who records a later start loses the single tax and levy of the quarters
before it. That is the intent: those figures were wrong, not provisional.

---

## ADR-024. Refuse an unsafe request a browser sent from another origin

Date: 2026-10-02

Status: Accepted

### Context

The audit (Security M1, Architecture H1) found that `docs/architecture.md` claimed an `X-Requested-With`
check that no code made. The only defence was `SameSite=Lax`, which stops other sites but not sibling
subdomains of `blonskyi.dev` (`todo`, `hub`, `plane`, and others), and many state-changing POSTs take no
body, so a cross-origin `fetch` sends them without a CORS preflight. A compromised sibling could verify tax
parameters, rotate the calendar feed, force syncs and send test messages with the owner's cookie.

### Decision

`CrossSiteGuard` runs after `UseForwardedHeaders` and before authentication. For POST, PUT, PATCH and
DELETE:

1. `Sec-Fetch-Site` present: the request passes only if it is `same-origin`.
2. Otherwise `Origin` present: it must equal `{scheme}://{host}` as the browser saw it, which
   `UseForwardedHeaders` restores from `X-Forwarded-*` (the host is pinned by `ALLOWED_HOSTS`).
3. Neither header: the request passes.

A refusal is a 403 ProblemDetails with `code: cross_site_request` (ADR-028).

Rule 3 is the decision to read carefully. Every browser that can attach the owner's cookie to an unsafe
request sends `Sec-Fetch-Site` (Chrome 76, Firefox 90, Safari 16.4) or at least `Origin` (all of them, on any
non-GET). A request with neither is not a browser, so it is a script, `curl` or a provider's server, and a
web page cannot make one of those carry the owner's cookie. Refusing it would break the e2e API calls and
the owner's own scripts and protect nothing. The residual hole is a browser too old to send either header,
which the app's own sign-in and CSP already do not support.

The only exempt path is the monobank webhook (`/api/monobank/webhook/{secret}`, ADR-012): the bank's server
posts to it, and the secret in the path is its credential. Nothing else needs it. The calendar feed and the
health check are GET, the Google and development sign-in callbacks are GET redirects, there is no inbound
Telegram webhook (the bot long-polls), and a passkey sign-in is a POST from the app's own page, so it
passes the check like any other.

### Alternatives considered

An `X-Requested-With` header set by the fetch wrapper and checked in the api. It works, but it is a
convention every future caller has to remember, and a request the wrapper does not make (a form, a link)
would fail in a way the browser's own headers never do.

Antiforgery tokens. They need a cookie, a header and a fetch-wrapper change for one owner and one origin.

Refusing a request with neither header. Stricter, but it blocks non-browser clients for no gain, see above.

Tightening `SameSite` to `Strict`. It does not stop a sibling subdomain, which is same-site.

### Consequences

The web client needed no change: the browser sets the headers, and the Next rewrite passes them to the api
untouched, which `web/e2e/cross-site.spec.ts` proves against the real stack by sending the headers a
browser would (Chromium refuses a cross-origin loopback request before sending it, so a real cross-site
browser request cannot be made in the suite). That spec covers only the Next hop; Traefik and Cloudflare in front of it are not exercised, and a header
they strip would go unnoticed there. The api tests that post without headers still pass because of rule 3. A reverse
proxy that strips `Sec-Fetch-Site` and `Origin` would silently disable the check, so the e2e spec is what
guards that path. A cross-origin request to the monobank webhook is accepted by design.

---

## ADR-025. The declaration screen lists the form's fields to copy, built from the XML's own list

Date: 2026-10-02

Status: Accepted, amended 2026-10-03 (the Cabinet does import XML, see the amendment)

### Context

ADR-016 assumed the owner imports the XML in the Electronic Cabinet. The new Cabinet has no import: the
owner creates the form ("Введення звітності" → "Створити" → F0103309) and types it in. The 2026-10-02 audit
(UX High 1 and 2) found the screen telling the owner to import, and its figures not copyable, so a phone
user retyped every line.

### Decision

`CabinetForm` builds the F0103309 body (and annex 1's) as one ordered list of `CabinetField`: the XSD
element, its text, where it sits on the form (`Part`), its kind, its printed line number and, for the
annex's table, its month and column. The XML writers write that list, skipping a field with no value, and
`GET /api/declarations/{year}/{quarter}` serves the same list as `cabinet`, so the view and the file cannot
differ; an api test pins it for a quarter with the annex, a crossing quarter and one without.

The value is the XML's text, so it is what the schema accepts: `DGdecimal2` is `-?[0-9]+\.[0-9]{2}`, a dot
and exactly two decimals. The owner copies `1234.56` and `0.00`. A line the XML omits has a null value: the
screen says to leave it empty. Dates are the only change, from `ddMMyyyy` to `dd.MM.yyyy`, which is how a
Cabinet date field reads. Marks (type, period, annex boxes) are ticked, not typed. Labels live in the web's
message catalogs, keyed by the element; the api sends no text (ADR-002). The XML card stays, retitled for
M.E.Doc and other software.

### Alternatives Considered

A separate endpoint with its own mapping from the figures. Two mappings can drift, which is the failure the
audit found in the guide. A client-side mapping of `DeclarationFigures` to lines: the web would need the
annex's rows and the header too, and would re-decide which lines the file omits.

Formatting amounts with a comma or spaces for reading. The Cabinet takes what the schema takes, and the
copy button removes any need to read the digits.

### Consequences

The Cabinet's own field captions are not in the schema; the labels are the form's line names the screen
already used, plus plain names for the header. The owner should compare the first filing with the real
form and the labels be corrected if they differ. Whether the Cabinet recalculates lines such as 08, 12 and
14 itself is unverified; the guide asks the owner to stop if its sums differ from the screen's.

### Amendment, 2026-10-03: the Cabinet imports the XML (#222)

The premise above was wrong. The Cabinet's "Введення звітності" editor has a "Завантажити" button: the owner
created the Q3 2026 group 3 declaration by importing the file this app generated, and found four header
gaps only after the import. The main path is the file again: "Введення звітності" → "Створити" (or open a
draft) → "Завантажити" → pick the XML (and annex 1 beside it) → check → sign → send. The screen leads with
it, for the Cabinet and for M.E.Doc alike.

The field list stays, for two jobs: a cross-check of what the Cabinet shows after the import, and the way to
type the form in if an import fails. Since the imported file is what the owner signs, the gaps that matter
are in the file, and #222 fills them: HEMAIL and HTEL from new declaration details, HNAME the full name with
the patronymic, the KVED class names, and HBOS as the given name and the surname in capitals. The list gains
a `Footer` part with HFILL and HBOS. HFILL (and D_FILL) stay the day the file is written, in Kyiv, because
the writer is a pure function of that date; the screen tells the owner to set the filing date in the
Cabinet to the day they send, when that is another day.

---

## ADR-026. Alert on a stalled sync or a bad token, once per incident, through the reminder channels

Date: 2026-10-02

Status: Accepted

### Context

A stopped bank sync is recorded (`BankAccount.LastFailure`, `MonobankConnection.RejectedAt`) but shown
only on the monobank settings tab. The dashboard does not mention it, and the notification channels carry
only tax reminders. The owner learns of a rejected token or a dead sync by opening a screen they have no
reason to open, while income, tax, the limit bar and reminder amounts quietly understate (audit of
2026-10-02, reliability H3 and UX High 3, #174).

### Decision

**Health is derived, not stored.** `SyncHealthCheck` reads the connection and the followed accounts and
answers one of `Healthy`, `Stale`, `TokenRejected` and `TokenUnreadable` (Rule 18). The dashboard and the
alert source both call it, so the card and the message cannot disagree. Staleness is 3 days since the
oldest caught-up cursor, a constant with its reason beside it, because it is an operational limit and not
a tax parameter (those live only in `TaxYearConfig`). A token problem outranks staleness.

**An incident is a key, and recovery re-arms it by itself.** The key is the kind plus the moment the state
began: the rejection's time, or the last good sync. The last good sync only ever moves forward when the
sync recovers, so a second incident gets another key without anything deleting the first claim. This
avoids a "resolved" write that a crash or a restore could lose, and it needs no state beyond what the sync
already keeps.

**The existing claim log carries it.** `SentReminder` gains an `Incident` column. A reminder has it empty
and keeps its unique index, now filtered to empty rows. An incident row has a second unique index on
(`UserId`, `Incident`, `Channel`), filtered to non-empty rows. The sender's claim, send and give-back
logic is one method used by both, so an incident inherits ADR-019's guarantees: at most once, a transient
failure releases the claim, a possible delivery keeps it. `Date`, `Kinds` and `Offset` mean nothing for an
incident and are filled with the claim day, none and `OnTheDay`. A separate table would repeat the claim
code, the retry rule and the channel lookup for no gain.

**Sources are pluggable, the sender is not.** `IIncidentSource` returns the incidents open for an owner;
`ReminderSender` asks every registered source after its reminder pass. The Notifications feature defines
the interface and the Monobank feature implements it, so Notifications never references Monobank. A failed
backup (#176) or an expired Treasury account (#173) is a new `IncidentKind`, a text and a source, and no
change to the sender.

### Alternatives Considered

Store an `Open` and `ResolvedAt` per incident in a new table. It would give a history, but it adds a write
on every recovery and a way to disagree with the sync's own state.

Repeat the alert daily while open, as the audit proposed. The ticket asks for at most once per incident;
the dashboard card stays visible, and a daily message about a bad token is noise the owner mutes.

Send only at 09:00 like a deadline. A deadline has a date to anchor to; an incident does not, and a
rejected token is worth knowing about at once. The runs are 5 minutes apart already.

Put the staleness threshold in settings or `TaxYearConfig`. It is not the owner's choice and not a tax
fact; a constant is easier to test and to change in one place.

### Consequences

One migration adds the column and swaps the unique index for two filtered ones. The dashboard response
gains a nullable `sync`. An account still backfilling is judged by its last progress, its latest
import batch (#199), or by `BackfillStartedAt` (added, followed again or reset by a restore), whichever is later, against the same 3 days, so a
backfill that keeps failing without a token error goes stale and alerts once like any other incident. A
stale or unreadable-token incident is held back while any followed account is queued or syncing, so a
recovery under way (accounts recover one at a time and move the oldest cursor) does not re-key it; if an
account is still stale once the queue empties it is alerted under its own key. `RejectedAt` is set only
while null, so a repeat 401 cannot re-key a rejection. An alert can arrive at any hour.

---

## ADR-027. Dump the database before a migration runs, and make CI wait for the new release

Date: 2026-10-02

Status: Accepted

### Context

The audit (Reliability H1, M1, M3) found that every green push to `main` deploys itself and that `api` runs
`MigrateAsync` on start. Migrations only move forward, so a bad one could be undone only from the daily
instance-wide `pg_dumpall` (up to 24 hours of loss, and every other project restored with it) or from a
`pg_dump` the owner was supposed to take by hand. The deploy job passed on the webhook's HTTP 200, so an
unhealthy release went unnoticed. Compose set no memory limit, log rotation or restart policy on a 7.7 GB VPS
shared with about six other projects.

### Decision

1. **Dump at startup, in `api`.** Before `MigrateAsync`, `MigrationDump.MigrateAsync` asks EF for the pending
   migrations. With none, it does nothing. With any, it runs `pg_dump --format=custom` of the app database into
   `Migrations__DumpDirectory` (the `migration-dumps` volume), writing `<name>.partial` and renaming it only
   when the file is non-empty. The name is `taxes_ua-pre-migrate-<UTC time>-from-<last applied>-to-<last
   pending>.dump`. It keeps the newest 10 (`Migrations__DumpKeep`). If a dump with the same from/to already
   exists (a restart loop), no new one is taken, so a loop cannot prune the older dumps.
2. **A failed dump stops the migration.** The dump error is logged as critical and rethrown, so the process
   exits (`init: true`) with the database still on the old schema. The site is down until the dump works or a
   fix is deployed; that is the price of never migrating without a way back. An unset
   `Migrations__DumpDirectory` (a local run, the tests) logs a warning and migrates without a dump;
   `deploy/check-compose.sh` asserts the production compose sets it.
3. **No new secret.** `pg_dump` reads the host, database, user and password from the connection string `api`
   already has, passed as `PG*` variables and not on a command line. The image installs `postgresql-client-18`
   from the PostgreSQL apt repository, because `pg_dump` must be at least the server's major version and
   Debian's is older. When the shared instance moves to a newer major, bump the number in `api/Dockerfile`.
   `deploy/smoke-test.sh` runs the real dump against a PostgreSQL 18 container.
4. **The release is in `/api/health`.** `api` reads Coolify's injected `SOURCE_COMMIT` (or `App:Release`) and `/api/health` returns it as `release`
   (`unknown` when unset). The compose file must not mention `SOURCE_COMMIT`: Coolify turns a mention into an
   empty user variable, which stops it injecting the commit. After the webhook, the
   deploy job polls `https://taxes.blonskyi.dev/api/health` every 15 seconds for 15 minutes and passes only
   when `status` is `ok` and `release` matches the commit being deployed (either one a prefix of the other, and the release at least 7 characters).
   Health alone would pass on the old release still answering.
5. **Compose.** Both services get `restart: unless-stopped`, `mem_limit` (512 MB `api`, 384 MB `web`) and
   `json-file` logging with `max-size: 10m`, `max-file: 5`. The deploy job joins the `deploy-coolify`
   concurrency group without cancelling a run in progress.

### Alternatives considered

A separate compose init service for the dump: it needs its own image, the database credentials a second time,
and a way to know whether a migration is pending, which only the app can tell. A Coolify pre-deploy command:
it runs outside the repository and cannot see pending migrations either. Dumping on every start: it fills the
volume with identical files on each restart. A CI check that fails when `Data/Migrations/` changed: it
leaves the dump to the owner's memory, which is the failure being fixed. Streaming the dump to MinIO: it needs a
key in the compose environment and shares the VPS disk anyway.

### Consequences

The dump volume lives on the VPS disk. It protects against a bad migration, not against losing the server
(section 8 of `docs/deploy.md` covers that). Rolling back to an old image does not undo a migration, so a
rollback of a release with a migration restores the matching dump (see "Rollback"). The workflow-level `ci`
concurrency still cancels an older run on `main` when a newer push arrives, including its deploy job while it
polls; Coolify keeps deploying, and the newer run reports the result. A run for commit A whose webhook builds a
newer `main` never sees A in `/api/health` and fails at the timeout, although the newer release is up.


---

## ADR-028. Name every API failure with a stable code, and translate it in the web by that code

Date: 2026-10-02

Status: Accepted

### Context

The audit (Architecture H3) found that the error contract was free English text. Of about 70 `Results.Problem`
calls only two carried a machine-readable `reason`, and nothing in the web read it. Field errors were English
sentences repeated with different wording in a dozen places, and the web translated them with
`message.includes("exceed" | "control character" | ...)` in four files, and showed the rest as they came.
Rewording a server sentence silently broke a Ukrainian or Russian message, and a failure nobody had written a
pattern for reached the owner in English.

### Decision

1. **Every failure the app writes carries a `code`.** One helper, `Problems` (`api/src/TaxesUa.Api/Problems.cs`), writes every
   failure response; no other code calls `Results.Problem` or `Results.ValidationProblem` (a test scans the
   sources). `Problems.Create` writes a ProblemDetails with the extension `code`; `Problems.Validation` writes a
   400 (or 422) ProblemDetails whose `code` is `validation_failed` unless a more specific one is given
   (`invoice_incomplete`, `backup_invalid`, `prototype_invalid`). `title` and `detail` stay English sentences for
   logs and for a person reading a response; the web never shows them. The failures the framework writes itself carry none: a malformed JSON or binding 400, the bodyless 401,
   the 400 for a forwarded host outside `ALLOWED_HOSTS`, and an unhandled 500. The web shows the `unknown`
   sentence for them. The `type` URIs of the earlier problems are
   gone: the code replaces them. The `reason` extension of the declaration file refusals became `code`
   (`quarter_not_ended`, `file_generated_before_quarter_end`); extra data stays an extension
   (`availableFrom`, `missingInvoices`).
2. **A rejected body carries a code per field error.** `errors` (field to English sentences, ASP.NET's own
   shape) is kept, and `errorCodes` holds the code of each sentence at the same index, keyed by the same field
   path. Validators build a `FieldErrors` (`Set`, `Add`, `Merge`) instead of a dictionary of sentences, so a
   sentence cannot be written without a code. A check that explains itself returns an `Issue` (code and
   sentence), such as `IbanProblem` or `SignatureError`.
3. **One list of codes.** `ProblemCodes` is a single static class of snake_case constants, grouped by the part of
   the app. A code names the reason, not the field: `too_long`, `required`, `control_character` serve every text
   field, and a reason with its own wording has its own code (`iban_checksum`, `rnokpp_invalid`,
   `registration_date_after_group3_receipt`). A test keeps every constant used and snake_case.
4. **The OpenAPI document says so.** Endpoints declare `ProducesCodedProblem(status)` and `ProducesFieldProblem()`,
   which publish `CodedProblemDetails` and `FieldProblemDetails`, so `openapi-typescript` types `code` and
   `errorCodes`.
5. **The web translates by code.** `ApiError` exposes `code`, `fieldCodes` and `extensions`, and no longer the
   sentences. The catalog `apiErrors` in `web/messages/{uk,ru}.json` is keyed by code, one flat namespace, plus
   `unknown`. `useApiErrorText` (`web/src/data/api/`) turns a failure, a field's codes or a screen's own
   "could not save" followed by the reason into text. A code the build has no words for, a failure with no code and
   a thrown value that is not an `ApiError` all read as the generic sentence, never as the API's English. Screens
   whose layout depends on what failed still branch on the status or the code, not on the text.
6. **Two tests keep it that way.** The catalog test reads `ProblemCodes.cs` and fails when a code lacks a Ukrainian
   or Russian text, or a text lacks a code. The guard test fails when a web source reads an error's `message`,
   `detail` or `title`, or contains an English sentence the API sends.

### Alternatives considered

Codes only in `errors`, in place of the sentences: the shape the generated types already express, but the logs
lose the sentences and the ASP.NET validation shape is no longer the one the tests and tools expect. A flat
`code` per response and no per-field codes: forms would still need the sentences to tell two failures of one
field apart. RFC 7807 `type` URIs as the code: a URI per reason is longer to write and compare, and the web has no
use for resolving them. Translating on the server by `Accept-Language`: the server would own two catalogs, the
`web` owns the language setting, and a cached response would carry the wrong language. A map from code to message
key per feature: the same code (`too_long`) would be mapped in a dozen places; one flat catalog is the simpler
rule.

### Consequences

Adding a failure means adding a constant and its two texts; the tests fail until both exist. Rewording a sentence, or changing it to name a limit, touches no web file. The texts carry no limits
(a text says "too long", not "at most 64 characters"), because the code carries none; where a limit matters the
screen's own hint says it. The English sentence of a field error is still useful in logs and in the API tests'
failure messages, and the tests assert codes. The backup and import file errors use a small set of generic codes
(`id_not_unique`, `unknown_reference`, `inconsistent_fields`, `duplicate_value`) beside the field path the screen
prints, since the owner reads them as a list of places in a file, not as prose. The declaration screen no longer
lists the XML schema checker's own messages: they are English diagnostics, which the api still sends in `errors` for the logs.

---

## ADR-029. The pay hero leads the dashboard, one banner at most above it, and every data screen shares one loading and failure state

Date: 2026-10-02

Status: Accepted

### Context

The audit (UX Medium 6, 9) found that up to four notices (limit crossing, review, declaration due, overdue
invoices) rendered above the pay hero, so on a phone "what do I pay" could fall below the fold. It also found
that loading and failure were a bare `<p>` per screen: not announced to assistive technology, and with no way
to try again except in `AuthGate`.

### Decision

1. **The hero comes first; the notices are ranked by consequence.** `web/src/features/dashboard/components/notices.ts`
   lists the notices in priority order and `activeNotices` returns the ones that apply. The first is the only
   banner above the hero; the rest fold into one closed "Needs attention (N)" `<details>` under it. The order,
   most consequential first:
   1. Sync rejected or unreadable (`TokenRejected`, `TokenUnreadable`). Income stopped arriving, so every figure
      depends on it and the owner pays too little (ADR-026).
   2. Limit crossing. The regime changes for the quarters it names.
   3. Group 3 unconfirmed, or its application deadline. Missing the deadline cannot be undone (Rule 8, ADR-023).
   4. Declaration due.
   5. Sync stale. Figures may be incomplete, but the feed still works.
   6. Transactions waiting for review.
   7. Overdue invoices. A client's late payment has no tax consequence; it is a collections matter.

   A debt is the hero itself (red when overdue), so it takes no banner. Treasury account expiry is shown inside
   the hero's pay panel, where the account is used, so it never takes a banner slot. The quiet "last exchange"
   line of a healthy feed is not a notice and stays under the hero.

   A declaration, or a group 3 application, with three days or fewer left (or already past) is promoted above all
   the others, keeping the order above between two promoted ones.

   The folded list's summary shows the count and the titles of what it holds, and takes the colour of the most
   severe of them (red if any is an alert, amber if any is a warning), so a serious notice does not hide behind a
   neutral line.
2. **One loading and failure state, in two layers.** `LoadStateView` (`web/src/shared/ui/load-state.tsx`) is
   presentational: loading or offline status text, or a failure with its text and an `onRetry`. `LoadState`
   (`web/src/data/api/LoadState.tsx`) is the thin adapter over a TanStack query (or several a screen needs
   together); it lives in `@/data/api` because it words failures with `useApiErrorText` (ADR-028) and `@/shared`
   may not import `@/data`. Screens without a query object, such as the pay panel, use the view with their own
   refetch.
   - Loading and offline are a polite `role="status"`. A query paused for the network reads as an offline line,
     not as a retry that would do nothing.
   - A failure is a `role="alert"` worded as the screen's own "could not load" followed by the api's coded
     reason when this build has words for it. Its retry refetches only the failed queries.
   - TanStack resets a failed query that has no data to pending while it refetches, which would unmount the alert
     and drop keyboard focus. The adapter remembers it is retrying and keeps the alert and its button on
     screen until the refetch settles. The button is `aria-disabled`, not `disabled`, so focus stays on it, and
     reads "Retrying…".
   - Each retry button is described by its own failure text (`aria-describedby`), since a screen can show two.
   - `quiet` is for a side query a screen works without (the client and receipt suggestions in a form): it
     shows nothing while loading and the failure with a retry only if it fails, so a failed list is never
     mistaken for an empty one.
3. **A failure that used to read as an empty state is shown.** The invoice draft's client list, the
   transaction form's client and receipt suggestions, and the reserve jar (a failed load is not "no jar
   chosen") now show the failure and a retry.

### Consequences

A new data screen renders `LoadState` instead of writing its own `<p>`. A new dashboard notice is added to
`noticePriority` at its rank and to `noticeSeverity`, with a case in `NoticeView` and a title in
`useNoticeTitles`. The banner above the hero is chosen by rank alone, apart from the three-day promotion, so
the hero is never pushed down by more than one banner.
