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
web proxy. That is a new anonymous endpoint on an app whose only public surface is otherwise the login
page, to save a request that costs nothing when idle. It also makes a local run unable to receive
anything without a tunnel. Polling needs only an outbound HTTPS call, which the VPS and a laptop both
have.

### Consequences

Nothing new is exposed to the internet. One process may poll a given bot: a second instance, such as a
local stack started with the production token, makes Telegram answer 409 to one of them, so the local
stack must use its own bot or none. The api already runs as a single instance for the same reason as the
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

The app prepares the file and stops there. The owner imports it in the Cabinet ("Імпортувати XML з
пристрою"), checks it against the Declaration screen, signs it with a KEP and sends it. The app holds no
key, no Cabinet session and no DPS credential.

F0103309.xsd and common_types.xsd are vendored next to the writer, byte for byte, with their source,
commit, fetch date and hashes in a README, and embedded in the api. Every file is validated against them
before it is stored or downloaded; a file that fails is never handed out, and the owner sees the
validator's errors instead. The writer is a pure function of the declaration figures, the details and
the fill date, so golden files pin its output byte for byte. The last file per quarter and type is
stored with its generation time and travels in the backup as the record of what was prepared.

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
