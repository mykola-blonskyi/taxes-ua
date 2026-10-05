# Audit Report

Date: 2026-10-05

Auditor: four independent read-only reviews coordinated in one session (security and privacy, architecture and reliability, backend code review, web code review). The coordinator re-read the code behind the High finding and the two web findings with the widest reach before including them.

Audit Type: full application audit (architecture, security, code review)

Scope: `main` at d758079, after every ticket of the 2026-10-02 audit (#170, #171–#190) and the follow-ups #193 and #199 had closed. Baseline: [2026-10-02-full-audit.md](2026-10-02-full-audit.md). Production was touched only with `curl -sI` on `/api/health`. Checks run: engine tests (441 pass), architecture tests (26 pass), `pnpm test` (594 pass), `pnpm lint` (clean), `dotnet list package --vulnerable` (none), `pnpm audit --prod` (one build-time-only high, see Low).

---

## Executive summary

Of the 2026-10-02 audit's findings, almost all are fixed, and most are now held by tests rather than by docs: the cross-site guard, stable error codes, the feature-boundary test, the clock test, the pre-migration dump and the post-deploy health wait, sync-health alerts, the off-host backup with a weekly restore check. The engine still obeys its constraints: no packages, no `decimal`/`double`/`float`, no ambient clock, every rate from `TaxYearConfigInput`.

There is no Critical finding. One High and nine Medium findings remain, and none of them loses money silently on today's data. The High one is a crash path that real use will reach in January.

| # | Severity | Finding | Area |
|---|----------|---------|------|
| 1 | High | `GET /api/dashboard` returns 500 when the year's group 3 income is negative (a refund of last year's receipt before any new income). `LimitMonitor.Evaluate` throws on negative input (`LimitMonitor.cs:36`); `DashboardEndpoints.cs:97-99` passes the value unclamped | backend |
| 2 | Medium | A foreign-currency monobank receipt whose NBU rate lookup fails is skipped, and the sync position moves past it. Outside the 31-day re-read window it is never recorded, so income is understated (`MonobankStatementImport.cs:298-306, 336-342`) | backend |
| 3 | Medium | `POST /api/transactions` and `POST /api/payments` take no owner lock, unlike PUT, DELETE and confirm. Two concurrent refunds can both pass the over-refund check; a typed payment can race a bank-candidate confirm (`TransactionsEndpoints.cs:83-128`, `OwnerLock.cs:6-9`) | backend, architecture |
| 4 | Medium | A failed background refetch replaces nine settings forms and the invoice editor with "load failed", throwing away unsaved edits (`query.isError || !data` guards) | web |
| 5 | Medium | Sign-out and passkey sign-in clear only the `me` query; a second allowlisted user in the same tab sees the first user's cached data for up to 60 s (`useSignOut.ts:15`, `usePasskeyCeremony.ts:135-139`) | web, security |
| 6 | Medium | The dashboard stays stale after a monobank sync (`useMonobank.ts:25-28`) and after an invoice changes (`useInvoices.ts:60-64`); settings forms keep pre-restore values and Save writes them back | web |
| 7 | Medium | About ten mutations show nothing on a network failure (non-`ApiError`); the button just re-enables, so the owner may think it saved | web |
| 8 | Medium | Settings language, theme and default currency are saved but never read by the UI | web |
| 9 | Medium | The feature-boundary test misses constants read from shared code: `CrossSiteGuard.cs:44` reads `MonobankWebhooks.PathPrefix` and the test passes. Settings is becoming a shared home for Notifications types (#232) | architecture |
| 10 | Medium | API tests and CI run Postgres 16; production runs 18 (`ApiFixture.cs:43`, `ci.yml`, `docker-compose.local.yml`, README) | architecture |

The full findings from each review follow, unedited apart from section headings. Each carries file:line references, the failure scenario and a proposed fix.

## Not fixable in code (owner actions and decisions)

- Confirm on the host that ports 80/443 accept only Cloudflare, that Traefik and Cloudflare pass `Origin` and `Sec-Fetch-Site` through (otherwise the cross-site guard is off), and whether host or Coolify snapshots include the dump and key-ring volumes.
- Decide whether the prototype importer (about 1,200 lines with tests, plus a UI panel, built for a guessed file format) was ever used. If not, delete it.
- Decide a support floor for backup schema versions (now v18), so upgrade steps below it can be deleted.
- Decide whether the learned 2026 temporary military-levy account should expire by default instead of only when the owner taps the end date (#173 follow-up).
- Three Low findings from 2026-10-02 were never ticketed or declined (session cookie prefix and sliding expiry, plain secrets at rest, tax-year parameters not scoped to an owner). Ticket or decline them.
- 31 stale agent worktrees under `.claude/worktrees` take about 21 GB.

## Action plan

- [ ] A. Clamp negative income before `LimitMonitor.Evaluate`, and compare the warn threshold in kopecks (backend #1, #3 Low).
- [ ] A. Take the owner lock in `POST /transactions` and `POST /payments`.
- [ ] A. Do not advance the monobank sync position past a window with a rate failure.
- [ ] B. Web: guard forms on `!data` only; `queryClient.clear()` on sign-out and sign-in; invalidate the dashboard after sync and invoice changes; reset settings forms after a restore; show an error on network failures.
- [ ] B. Apply or relabel the three unused settings.
- [ ] C. Close the boundary-test gap for shared code; move Notifications types back; run tests on Postgres 18.
- [ ] D. Lows: encrypt pre-migration dumps; reject future-dated backups and restore as a non-superuser; log `account.Id`, not the monobank id; ignore `*.dump*` and root images; reminder send with its own timeout instead of the shutdown token; docs drift (audit-log list, web feature list, `graph/architecture.md`).

---

# taxes-ua security and privacy audit, 2026-10-05

Scope: `main` at d758079, read-only. Baseline: `reports/audits/2026-10-02-full-audit.md` (security section, at 39ce9bd).
New code reviewed: the 31 commits in `39ce9bd..HEAD`. The focus was CrossSiteGuard, the backup sidecar, the pre-migration
dumps, CI, CSP and the service worker, and the new declaration fields.
Method: code reading, a grep sweep of every route's authorization, `pnpm audit --prod` and
`dotnet list package --vulnerable --include-transitive`, and a pattern scan of the added lines in `git diff 39ce9bd..HEAD`.
The scan looked for IPs, IBANs, RNOKPP, age keys, bot, Google, GitHub and AWS tokens, and PEM blocks.
Production was touched once: `curl -sI https://taxes.blonskyi.dev/api/health`. It returned 405 for HEAD, with HSTS,
nosniff, XFO DENY and Referrer-Policy present.

**Summary.** There are no Critical or High findings, and no Medium findings in new code. M1 (CSRF) and M2 (origin IP) of
the previous audit are fixed. Of its nine Low findings, five are fixed and one (L8) is fixed only in part. Three were
never ticketed (L3, L9, L10), and #180 did not list them, so they are still open. The new code has four Low issues. Two
of them concern backups: pre-migration dumps that are not encrypted, and backup objects whose origin is not
authenticated. The other two are monobank account ids still in the app logs, and real dump files that `.gitignore`
does not cover.

---

## 1. Status of the 2026-10-02 security findings

| # | Finding | Status | Evidence |
|---|---|---|---|
| M1 | No CSRF check; body-less POSTs forgeable from sibling subdomains | **Fixed** | `api/src/TaxesUa.Api/CrossSiteGuard.cs:33-62`: an unsafe method needs `Sec-Fetch-Site: same-origin`, or else an `Origin` equal to the public origin. It is wired at `Program.cs:302`, after UseForwardedHeaders and before authentication. The webhook exemption is exact, `/api/monobank/webhook/` (`MonobankWebhooks.cs:37-39`). ADR-024 and `web/e2e/cross-site.spec.ts` cover it. |
| M2 | Origin VPS IP published in docs/deploy.md | **Fixed in the tree** | No non-loopback IPv4 in `docs/deploy.md`. History still holds the IP (accepted). Nothing in the repo shows whether 80/443 are firewalled to Cloudflare's ranges; verify on the host. |
| L1 | Dev sign-in gated only by environment name | **Fixed** | `Program.cs:310-322` needs Development **and** `Auth:DevelopmentSignIn=true`. `Program.cs:55-61` refuses Development together with a pinned domain. `deploy/check-compose.sh:15` asserts Production. |
| L2 | Local stack on all interfaces | **Fixed** | `docker-compose.local.yml:7` binds `127.0.0.1:${WEB_PORT}`. `check-compose.sh:52` asserts it. |
| L3 | Session cookie not `__Host-`; 14-day sliding expiry | **Open, never ticketed** | `Program.cs:243-258` still names the cookie `taxesua.auth` and sets no `ExpireTimeSpan`. #180 did not list it. |
| L4 | CSP `unsafe-inline`, no Permissions-Policy | **Fixed** | `web/src/shared/security/csp.ts:5-21` uses a nonce with `'strict-dynamic'`. `web/src/proxy.ts:24-30` mints the nonce per request. Static paths get `staticContentSecurityPolicy` (`next.config.ts:29-36`). Permissions-Policy is at `next.config.ts:10`. |
| L5 | Actions pinned by tag, no Dependabot | **Fixed** | Every `uses:` is pinned by SHA (`ci.yml`, `.github/actions/setup-web/action.yml`). `persist-credentials: false` is set. `.github/dependabot.yml` covers actions, nuget and npm. Base images are still unpinned (declined in #208). |
| L6 | Coolify response body in public CI log | **Fixed** | `ci.yml:247-252` prints only the status. See N5 for a related doc leak. |
| L7 | Restored backup keeps a confirmed Telegram chat | **Fixed** | `NotificationChannelBackup.cs:43-45` restores every channel with `Enabled = false` and `ConfirmedAt = null`. |
| L8 | Monobank account ids in logs | **Partly fixed** | The HttpClient loggers are removed (`Program.cs:160`), but app logs still write the id. See N3. |
| L9 | Secrets at rest in plaintext (key ring, feed and webhook secrets, AES-GCM without AAD) | **Open, never ticketed** | `Program.cs:92` persists the key ring with no encryptor. `CalendarFeed.cs:18,22` and `MonobankWebhooks.cs:80` store raw secrets. N1 adds plaintext DB dumps to the same disk. |
| L10 | Tax-year parameters global, not owner-scoped | **Open, never ticketed** | `TaxYearEndpoints.cs:32` has a group with `RequireAuthorization()` and no owner filter. Acceptable while the allowlist holds one person. |
| Info | Root `reserve-card-ru-375-surplus.png` not ignored | **Open** | Still untracked in the repo root. `.gitignore` has no `/*.png`. |
| Info | AI attribution trailers | **Clean since 39ce9bd** | No `Co-Authored-By: Claude` or "Generated with" in `git log 39ce9bd..HEAD`. |

---

## 2. New findings

### Critical / High / Medium

None found.

### N1 (Low). Pre-migration dumps are full plaintext copies of the database, kept on the VPS disk

- **Where:** `api/src/TaxesUa.Api/Data/MigrationDump.cs:131-150` runs `pg_dump --format=custom` with no encryption. The
  output goes to the `migration-dumps` volume (`docker-compose.yml`, `Migrations__DumpDirectory`), and up to 10 copies
  are kept (`MigrationDump.cs:18`).
- **Scenario:** ADR-031 encrypts the nightly backup so that a leaked bucket or bucket key "is not enough to read a
  backup". The pre-migration dumps go around that. Each one holds the whole database in clear: RNOKPP, IBANs, full
  name, phone, client data, the calendar and webhook secrets, Identity rows and passkey public keys. One is written on
  every release that carries a migration, which happens most weeks. They are exposed in three ways:
  - A host-level volume backup (Coolify or provider snapshots) copies them out unencrypted.
  - A future change that mounts or ships that volume elsewhere exposes them.
  - The rollback runbook (`docs/deploy.md:508`) streams them over ssh. That is fine, but they also sit on disk
    indefinitely.

  The directory is `0700` and owned by the app uid (`api/Dockerfile`), so the realistic attacker is someone with
  root or volume access. That attacker can also read `DATABASE_URL`, which is why this is Low.
- **Fix (minimal):** Pipe the dump through `age -r $BACKUP_AGE_RECIPIENT` (the recovery public key is already in
  Coolify), or at least lower `DumpKeep` to 2-3. Then document in ADR-027 that the dumps are plaintext.

### N2 (Low). Backups are encrypted but not authenticated; a leaked bucket key can plant a "newest" dump that the restore check runs as a superuser

- **Where:**
  - `deploy/backup/backup.sh:318` picks the newest object by name sort.
  - `:327` rejects only objects *older* than 48 h, so a future-dated name passes.
  - `:341-343` decrypts with the check key and runs `pg_restore` into a scratch cluster as the `postgres` superuser.
- **Scenario:** age encrypts to a public key, so anyone can encrypt to it and nothing proves who made a file. An
  attacker needs two things:
  1. the bucket key (the threat ADR-031 is built for)
  2. the check public key (not secret by design, and derivable by anyone who once saw Coolify's env)

  With those, the attacker uploads `daily/taxes_ua-29991231T000000Z.dump.age`. A custom-format dump can carry
  arbitrary SQL, such as `COPY ... TO PROGRAM`. Restored as a superuser, that SQL runs shell commands in the
  sidecar, and the sidecar holds `DATABASE_URL` and both S3 key pairs. The future-dated name also wins every later
  check, which hides real backup failures.

  A disaster restore that takes "the newest object" (`docs/deploy.md:435`) would load the attacker's data. That
  restore runs with `--role=taxes_ua_app`, so it does not get superuser.
- **Fix (minimal):**
  - Ignore objects whose timestamp is in the future.
  - Run the scratch restore as a non-superuser role (`createuser restore`, then `pg_restore --role=restore`).
  - Optionally record the SHA-256 of each uploaded object in `DatabaseBackupRuns.Detail`, and have the check and the
    runbook verify that the object matches a hash the app recorded.

### N3 (Low). Monobank account ids still reach the logs at Warning (L8 is fixed only in part)

- **Where:** `api/src/TaxesUa.Api/Features/Monobank/MonobankStatementImport.cs:146`, `:162-164` and `:181-182` log
  `account.ExternalId` on the rate-limit, unavailable and too-many-in-one-second paths.
- **Scenario:** The fix for L8 removed the HttpClient request logging so that the statement URL, and the account id in
  it, stopped reaching Coolify logs. The app's own warnings still print the same id each time monobank is unavailable
  or rate-limits. Those are routine events during a backfill.
- **Fix:** Log `account.Id`, the internal GUID that `RecordFailureAsync` already uses, instead of `ExternalId`.

### N4 (Low). `.gitignore` does not cover real dump files, and the restore runbook writes one in an unspecified working directory

- **Where:**
  - `docs/deploy.md:440` writes `age -d ... -o taxes_ua.dump`, and `:410` handles `taxes_ua-<time>.dump.age`, in no
    stated directory.
  - `.gitignore` ignores `*.key` but not `*.dump`, `*.dump.age` or `/*.png`.
- **Scenario:** The repository is public. Suppose the owner runs the restore runbook, or saves a screenshot with real
  figures (as `reserve-card-ru-375-surplus.png` already sits there), from the repo root. A later `git add -A` or
  `git commit -a` then publishes a decrypted full-database dump, or the screenshot. The runbook's step 5 ("delete
  `taxes_ua.dump`") is the only guard.
- **Fix:** Add `*.dump`, `*.dump.age`, `*.dump.partial` and `/*.png` to `.gitignore`. Have the runbook `cd "$(mktemp -d)"`
  first.

### N5 (Info). The Coolify resource uuid is published in docs

- **Where:** `docs/deploy.md:398` (`name='^backup-tpx1vnmef2rgcbjjpqlbvour'`), added in e32a6f2.
- **Note:** L6 removed this same kind of id from public CI logs. It is harmless without the Coolify token. For
  consistency, replace it with `<app-uuid>`.

### N6 (Info). `shadcn` CLI is a production dependency and brings a vulnerable `braces`

- **Where:** `web/package.json` (`"shadcn": "^4.21.0"` under `dependencies`). `pnpm audit --prod` reports 1 high:
  `braces <=3.0.3` (GHSA-vfj7-8cjw-p6xm), via `shadcn>fast-glob>micromatch`.
- **Impact:** Build-time only. Runtime imports just `shadcn/tailwind.css` (`globals.css:3`), and the standalone bundle
  does not include the CLI, so it is not exploitable. It keeps `pnpm audit --prod` red, though, which hides real
  alerts.
- **Fix:** Move `shadcn` to `devDependencies`, or override `braces` to `>=3.0.4`.

### N7 (Info). Offsite backup key has delete rights

- `backup.sh:250-256` prunes with `rclone delete`, so the key must allow `DeleteObject`. The same holds for the MinIO
  policy at `docs/deploy.md:373`. A VPS or Coolify compromise can therefore wipe the offsite history too.
  `docs/deploy.md` (step 3) already recommends object lock or versioning of at least 14 days. When the offsite target
  is enabled, make that a requirement rather than an option.

---

## 3. Checked and fine

- **Authorization coverage.** I checked every `Map*` call in `api/src/TaxesUa.Api/Features`:
  - Every group carries `.RequireAuthorization()`.
  - Every route mapped directly on the root carries it per route (dashboard, audit, payment-details,
    import/prototype, `auth/me`, passkey register).
  - The invoice partials (`InvoicesEndpoints.Lifecycle.cs`, `.Receipts.cs`) map onto the authorized group.
  - Anonymous routes:
    - `/api/health`
    - `auth/login/google`, `auth/callback` and `auth/logout`
    - `passkey/login/options` and `passkey/login`
    - the monobank webhook GET and POST (`MonobankEndpoints.cs:261-278`)
    - the calendar feed `/{secret}.ics` (`CalendarEndpoints.cs:90-106`)
  - The dev seam is mapped only under the two gates.
- **CrossSiteGuard logic.** GET, HEAD, OPTIONS and TRACE are exempt. `Sec-Fetch-Site` values `same-site`, `cross-site`
  and `none` are refused. The Origin fallback compares against the scheme and host restored by forwarded headers, and
  that host is pinned by `ALLOWED_HOSTS` (`Program.cs:94-100`). A leftover `X-Forwarded-Host` gets 400
  (`Program.cs:284-288`). The only exemption is the webhook POST, which reads no body and can only queue a sync.
- **Webhook and feed secrets.** Both are 32 CSPRNG bytes in hex (`CalendarFeed.cs:22`, `MonobankWebhooks.cs:80`). An
  unknown value gets 404. Both are excluded from OpenAPI. Request-line logging is held at Warning (`Program.cs:33`).
  The feed is `no-store`.
- **Monobank token.** Logs carry only `OwnerId`, the method and the failure kind. The token, the URL and the response
  body are never logged. The client has `RemoveAllLoggers()`.
- **Telegram and email.** The Telegram client has `RemoveAllLoggers()`. Its warnings carry only the method and the
  `HttpRequestError` kind (`TelegramClient.cs:88-140`). SMTP logs carry only the failure and the exception type.
  Incident messages (`IncidentTexts.cs`) are fixed texts plus a date and a settings link. No backup `Detail` and no
  amounts go to third parties.
- **New declaration fields (#222).** Full name, phone and email are validated (`DeclarationDetailsEndpoints.cs:207-221`:
  length, control characters, phone regex, email normalisation). They are written through `XmlWriter.WriteString`
  (`DpsXml.cs:274-288`). Schema reading still uses `DtdProcessing.Prohibit` with `XmlResolver = null`.
- **Open redirect and DOM sinks.** No `dangerouslySetInnerHTML` or `innerHTML` in `web/src`. The only `window.open`
  goes to the app's own invoice PDF URL. `returnUrl` still goes through `LocalRedirect`.
- **Error leakage.** No exception `.Message` reaches a response, apart from the WebAuthn failure text and a log string
  in `NbuRateClient`. There is no developer exception page.
- **Service worker** (`web/service-worker/sw.ts`). It precaches only the build output and `/offline.html`. Navigations
  are `NetworkOnly`. Nothing caches `/api`. `skipWaiting` is false, so an update waits for the owner's prompt.
- **Security headers live.** HSTS includeSubDomains, nosniff, XFO DENY and Referrer-Policy are set on `/api/health`.
- **Backup sidecar secrets.**
  - Secrets go through environment variables or process substitution, never argv.
  - The rclone remotes are configured through env, with no config file holding a key.
  - The scratch cluster uses a unix socket in a `mktemp` dir with `listen_addresses=''`.
  - It runs as `USER postgres`.
  - Stale scratch data is wiped at start.
  - The api image runs as `$APP_UID`, with the key and dump dirs at mode `0700`.
- **CI** (`.github/workflows/ci.yml`).
  - No `pull_request_target` and no `workflow_run`. Top-level permissions are `contents: read`.
  - The deploy runs only on a push to `main`.
  - Secrets are passed through env and only their length is echoed. The Coolify body is not printed.
  - No `${{ github.event.* }}` or other untrusted input is interpolated into `run:`. The matrix filters are static
    literals.
  - A fork's GHA cache writes are ref-scoped, so they cannot poison `main`.
- **Docker build context.** `.env` sits at the repo root, outside both build contexts (`./web`, `./api`).
  `web/.dockerignore` excludes `.git`.
- **Dependencies.**
  - `dotnet list package --vulnerable --include-transitive`: none in any of the 4 projects.
  - `pnpm audit --prod`: only N6.
  - `cn@0.4.0` is the legitimate `shadcn-ui/cn` package.
- **Git history since 39ce9bd.**
  - No tokens, `GOCSPX-` values, PEM blocks or AWS or GitHub keys.
  - The only age string is the literal prefix `AGE-SECRET-KEY-1` in the docs.
  - IBANs are registry examples or the public Treasury accounts (MFO 899998).
  - RNOKPP is `1234567890` only. The phone is `+380501234567` or `050.123.45.67`. The name is `Тестенко Тест Тестович`.
  - The backup fixtures v04-v18 hold synthetic data only (`Acme GmbH`, `*.example`).
  - No binaries were added apart from the offline page assets. Every author is the owner.

## 4. Not verified (needs the host)

- Whether 80/443 on the VPS accept only Cloudflare's ranges (this decides how much the M2 history leak still matters).
- Whether Coolify or provider snapshots include the `migration-dumps` and `dataprotection-keys` volumes (N1, L9).
- Whether Traefik or Cloudflare strip `Sec-Fetch-Site` or `Origin`. ADR-024 notes that e2e covers only the Next hop.
  A quick check is a cross-origin `fetch` from a sibling subdomain that should get 403 `cross_site_request`.


---

# taxes-ua architecture audit (read-only, main @ d758079, 2026-10-05)

Scope: `api/src/TaxesUa.Engine`, `api/src/TaxesUa.Api`, `api/tests/.../Architecture`, `web/src`, CI, compose,
docs (`docs/architecture.md`, `docs/decisions.md`, `docs/TODO.md`, `graph/*.md`). Paths are relative to the repo
root. The architecture tests were run once: `dotnet test --filter Architecture` gave 26 passed. Nothing in the repo
was edited.

Overall: the 2026-10-02 audit's architecture findings are almost all fixed, and fixed with structure
(tests that fail the build), not with prose. The remaining issues are smaller. There is one escape in the boundary
test that has already happened. One documented locking invariant is not held. Settings is starting to act as a
shared kernel. The backup schema chain is growing by one version a day. Some docs have drifted.

## Severity summary

| # | Severity | Finding |
|---|---|---|
| F1 | Medium | `POST /api/transactions` skips the owner lock; the Rule 8 refund cap and the OwnerLock invariant can be broken |
| F2 | Medium | Boundary test escape: shared code reaches `Features.Monobank` through a const, and the test passes |
| F3 | Medium | Settings becomes the place cycles go to die (NotificationChannel and its delivery policy moved there for one prefill read) |
| F4 | Medium | Backup schema reached v18 in 8 days, and every additive field costs an upgrade step plus a fixture |
| F5 | Medium | Prototype importer (~730 src lines + 446 test lines + UI panel) is built on a guessed file format; likely dead |
| F6 | Medium | Tests run on PostgreSQL 16, production runs 18 |
| F7 | Low | Reliability items still open: no pool or lock timeout, no single-instance guard, reminder lost when a deploy cuts a send |
| F8 | Low | Docs drift (6 stale claims, list below) |
| F9 | Low | Three files sit just under the 600-line gate; partial-class splits keep the classes large |
| F10 | Low | Hygiene: 31 stale agent worktrees (21 GB), stray PNG, uncommitted TODO/plan edits |

## 1. Previous findings: status

### Architecture section (audit lines 797-896)

| Prev | Status | Evidence |
|---|---|---|
| H1 anti-forgery claim false | **Resolved** | `api/src/TaxesUa.Api/CrossSiteGuard.cs:31-63` (Sec-Fetch-Site, then Origin), wired at `Program.cs:302`; ADR-024; doc corrected at `docs/architecture.md:345-349` |
| H2 `internal` boundary claim false, dense cycles | **Resolved, with one escape (F2)** | `api/tests/TaxesUa.Api.Tests/Architecture/FeatureBoundaryTests.cs:15-36` allow-list, `:150-174` acyclicity, `:194-207` shared-code check; ADR-008 amended (`docs/decisions.md:237`) |
| H3 free-text errors parsed with `includes()` | **Resolved** | `ProblemCodes.cs` (201 lines), `web/src/data/api/client.ts:7-51` reads `code`/`errorCodes`; grep for `message.includes(` in `web/src` returns nothing; `web/src/data/api/noEnglishApiText.test.ts` guards it |
| M1 giant Map*Api methods | **Resolved** | `InvoicesEndpoints.cs` 408 + `.Lifecycle` 197 + `.Receipts` 234; `TransactionsEndpoints.cs` 449 + 3 partials |
| M2 advisory lock copied 5x | **Resolved** | `api/src/TaxesUa.Api/Data/OwnerLock.cs:13-14`, 20 call sites |
| M3 `Missing(id)` copied 7x | **Resolved** | `Problems.cs:33-37`, 22 uses. The bare `Results.NotFound()` left (`TreasuryAccountsEndpoints.cs:60,107,154,209`, `DeclarationsEndpoints.cs:249,264`, `InvoicingEndpoints.cs:126`) are file downloads or route-enum misses, so they are fine |
| M4 `DateTimeOffset.UtcNow` in 11 places | **Resolved, enforced** | `Architecture/ClockTests.cs:12-38` fails on any ambient clock read in Api or Engine outside Program |
| M5 docs drift | **Mostly resolved** | ADR statuses now Accepted; see F8 for the new drift |
| M6 BackupDocument 1616 lines | **Resolved** | `BackupDocument.cs` 105 lines, split into `.Upgrade`/`.Validate`/`Backup*.cs` |
| M7 schema.d.ts drift not caught | **Resolved** | `.github/workflows/ci.yml:111` runs `.github/scripts/check-api-schema.sh` |
| M8 in-memory single-instance state | **Open (documented, not enforced)** | `docs/architecture.md:242`, `graph/dependencies.md:104-105`; no `pg_try_advisory_lock` or replica pin in the code or compose (F7) |
| L4 dead bits / stray PNG | **Partly open** | `reserve-card-ru-375-surplus.png` still untracked at the root; `NbuQr.MaxBytes` (`Engine/NbuQr.cs:24`) is used only by a test |
| L5 graph/*.md empty | **Resolved** | both filled in; `dependencies.md` table is test-enforced (`FeatureBoundaryTests.cs:177-189`) |
| L6 TanStack Table/Form claim | **Resolved** | `docs/architecture.md:36` |

### Reliability section (audit lines 649-795)

| Prev | Status | Evidence |
|---|---|---|
| H1 no pre-migration dump | **Resolved** | `Data/MigrationDump.cs:22-95` dumps or refuses to migrate, idempotent per from/to pair (`:62-67`); `api/Dockerfile:13-21` ships `postgresql-client-18`; `docker-compose.yml:57`; CI waits for the new SHA (`ci.yml:254-280`) |
| H2 no off-host backup or restore test | **Resolved (code side)** | `deploy/backup/` sidecar (ADR-031), `Features/DatabaseBackups/RestoreCheckIncidentSource.cs` alerts on a failed weekly check |
| H3 sync stops silently | **Resolved** | `Features/Monobank/SyncHealth.cs:27,80-101` (3-day stale, token rejected), `SyncIncidentSource.cs`, sent through the reminder pass (ADR-026) |
| M1 compose limits, logs, restart | **Resolved** | `docker-compose.yml:3-8,27-32,75-80` |
| M2 pool size, lock timeout | **Open** | `Program.cs:131-133` has no pool settings; `docs/deploy.md:120` has none in the connection string; `OwnerLock` waits with no `lock_timeout` (F7) |
| M3 deploy has no post-deploy check | **Resolved** | `ci.yml:254-280` |
| M4 reminder claim lost on restart mid-send | **Open, accepted by ADR-019** | `Notifications/ReminderSender.cs:213-251` (F7) |
| M5 NBU no retry | **Open, low** | `Program.cs:137-143`; rates are prefetched outside the lock (`MonobankStatementImport.cs:221-225`), so an outage gives an error, not a hang |
| M7 shallow health, no uptime probe | **Partly** | health now carries the release SHA; no external probe found in the repo |

## 2. Engine constraints: hold

- `api/src/TaxesUa.Engine/TaxesUa.Engine.csproj` has no `PackageReference` and no `ProjectReference`.
- A grep for `decimal|double|float|DateTime(Offset)?.(Now|UtcNow|Today)|Environment.|new Random|Guid.NewGuid|HttpClient` in Engine sources finds nothing.
- `ClockTests` now also loads `TaxesUa.Engine` (`ClockTests.cs:30`), so the clock rule is enforced, not only grepped.
- Rates, day counts and thresholds all come in through `TaxYearConfigInput` (`DeadlineCalendar.cs:16-28`). The only
  literals left are calendar structure (`12` months, quarter math), `Money.RateScale = 10_000`, and the NBU QR
  format constants (`NbuQr.cs:13-35`), which come from the spec and are not tax parameters.
- In the API, the only rate literals are the 2026 seed in `Features/TaxYears/TaxYearConfigConfiguration.cs:26-29`,
  and that is legitimate. There are no `new DateOnly(20xx…)` literals in Features.

## 3. Module boundaries

The test is real and hard to bypass by accident. It reads IL with generic arguments unwrapped
(`CompiledReferences.cs:76-102`), so `db.Invoices` from another feature is caught as `DbSet<Invoice>`. Lambdas and
state machines count against their outer type. A Roslyn syntax scan covers inlined consts and enums. Listed edges
must still be used (`FeatureBoundaryTests.cs:125-131`), and the graph doc is checked against the list. Every
`Features/<X>/*.cs` file declares `namespace TaxesUa.Api.Features.<X>`. I checked all of them, because the test
attributes by namespace, not by folder. There are no cycles: 82 edges, layers 0-7.

### F2 (Medium). Shared code reaches a feature through a const, and the test passes

- `api/src/TaxesUa.Api/CrossSiteGuard.cs:1` has `using TaxesUa.Api.Features.Monobank;`, and `:44` reads
  `MonobankWebhooks.PathPrefix` (a `const string`, `Features/Monobank/MonobankWebhooks.cs:39`).
- Why the test misses it: the IL scan cannot see an inlined const. The const scan runs only over
  `src/TaxesUa.Api/Features` (`FeatureBoundaryTests.cs:89`), and `Shared_code_reaches_no_feature` (`:194-207`) is IL-only.
  So `docs/architecture.md:193-196` and `graph/dependencies.md:19-21` ("shared code reaches no feature") are false
  today. The 26 tests pass.
- A second gap: the shared-code check matches the namespaces `TaxesUa.Api` and `TaxesUa.Api.Data` exactly
  (`:197`). A new `TaxesUa.Api.Common` (or `.Shared`) namespace would be usable by every feature and could itself
  reach any feature, with no check.
- Why it matters: shared code that reaches a feature lets any feature reach that feature without an edge on the
  list. The defect is harmless today, but it is the exact hole the test exists to close.
- Minimal fix:
  1. Run `ConstEdgeScan` over the non-Features sources too (excluding `Program.cs` and `AppDbContext.cs`), treating
     their "from" as shared.
  2. Change the namespace filter to `!StartsWith("TaxesUa.Api.Features.")`, still with the Program and
     AppDbContext exemptions.
  3. Move the webhook route prefix into shared code (or have `MonobankWebhooks` read the shared one), so the guard
     keeps no feature import.

### F3 (Medium). Settings is collecting what breaks cycles

- PR #232 moved `NotificationChannel`, `NotificationChannelKind`, `DeliveryFailure` and the delivery policy
  `DeliveryFailures.IsTransient` from Notifications to `Features/Settings/NotificationChannel.cs:1-61`. Its EF
  configuration stayed in `Features/Notifications/NotificationChannelConfiguration.cs:6-10`.
- The only Settings code that uses it is one prefill query: `Settings/DeclarationDetailsEndpoints.cs:242-246`
  (`ConfirmedEmailAsync`, which offers the confirmed email address as the default report email). Every other user
  is in Notifications, plus Audit and Backup.
- Settings already holds Kved (628 generated lines), invoicing, declaration details, DPS status and treasury-adjacent
  validation. 13 of the 19 features reach it.
- Why it matters: the pattern is "move an entity down a layer to satisfy the DAG". It turns the acyclicity rule into
  pressure to grow a grab-bag module. Notifications' retry policy now lives in a folder nobody reading Notifications
  would open.
- Minimal fix (deletion): drop `ConfirmedEmailAsync` and the response field. The web already loads the channels
  (`web/src/data/notifications/useNotificationChannels.ts`) and can prefill the form itself. Then move the four types
  back to Notifications. The edge Settings -> Notifications disappears, and nothing new is added.

## 4. Workers and concurrency

What holds:

- `ClockTests` enforces `TimeProvider` everywhere.
- Reminders recompute the plan each pass. The partial unique indexes on `SentReminders` make sends at most once
  across overlapping instances (snapshot: `(UserId, Date, Kinds, Offset, Channel) WHERE Incident = ''` and
  `(UserId, Incident, Channel) WHERE Incident <> ''`).
- Each monobank window is one transaction under the owner lock. FX rates are prefetched before the lock is taken
  (`MonobankStatementImport.cs:221-225`), and imports are idempotent by `(BankAccountId, ExternalId)`.
- After a restart, unfinished accounts are requeued (`MonobankSyncWorker.cs:85-102`). A missed 03:00 run is covered
  by the 31-day re-read plus the 3-day staleness incident.
- A failed pre-migration dump stops the deploy, and CI notices because the new SHA never reports healthy.

### F1 (Medium). `POST /api/transactions` takes no owner lock

- `Features/Transactions/TransactionsEndpoints.cs:83-128` (POST) calls `TransactionRecorder.RecordAsync` with no
  transaction and no `OwnerLock`. PUT (`:154-155`), DELETE (`:218-219`) and confirm (`:282-283`) all lock. The
  recorder itself (`TransactionRecorder.cs:15-53`) does not lock either. That is correct for the sync, which calls
  it inside its own lock (`MonobankStatementImport.cs:225,349`). This is not a regression: at 39ce9bd POST did not
  lock either.
- What can break:
  1. Rule 8's cap. `ValidateLinksAsync` sums the refunds already linked to a receipt (`TransactionsEndpoints.Links.cs`,
     `RefundExceedsReceipt`) and then inserts. Two concurrent refund POSTs, for example a double submit or two tabs,
     both pass under READ COMMITTED and together exceed the receipt.
  2. A POST that lands during a restore or a sync window interleaves with them. The usual outcome is a 500 from an
     FK or unique violation (a new client name colliding with one the import creates), not silent corruption.
- Why it matters: `Data/OwnerLock.cs:6-9` states that "the owner's own writes to rows those can change all take it".
  The most common write does not.
- Minimal fix: in the POST handler, look up the rate first, as PUT does at `:153`, then
  `BeginTransactionAsync` + `OwnerLock.AcquireAsync` around `RecordAsync`, and commit. The change is about 4 lines.
  Add one test that runs two parallel refund POSTs over a small receipt.
- Smaller cousins, Low: `Clients/ClientsEndpoints.cs` and `Settings/*Endpoints.cs` also write rows that restore
  replaces, without the lock. With a single owner the worst case is a 500 during a restore. Either lock them too, or
  narrow the OwnerLock doc comment to what is true.

### F7 (Low). Reliability items still open

- **Pool and lock timeouts.** `Program.cs:131-133` and `docs/deploy.md:120` set no `Maximum Pool Size`,
  `Timeout`, `Command Timeout` or `lock_timeout`, and `OwnerLock` can wait forever. Fix: append
  `;Maximum Pool Size=10;Timeout=15;Command Timeout=60` to the documented connection string, and add
  `SET LOCAL lock_timeout = '30s'` in `OwnerLock.AcquireAsync`.
- **Single instance.** It is stated in the docs and nowhere in the code. Fix if Coolify ever overlaps containers: the
  hosted workers take `pg_try_advisory_lock(<const>)` at start and idle without it. Until then, the docs are enough.
- **Reminder lost when a deploy cuts a send.** `ReminderSender.cs:234` passes the host's stopping token into
  `channel.SendAsync`, so a deploy that stops the process mid-send cancels the send and leaves the claim with
  `DeliveredAt` null for good. Main auto-deploys many times a day, and passes run every 5 minutes. Cheapest fix with
  no change to ADR-019: send with `CancellationToken.None` plus the channel's own timeout (SMTP has a 25 s cap), so
  graceful shutdown (30 s default) lets the in-flight send finish.

## 5. Data model and migrations

What holds:

- 34 migrations. Raw SQL appears only in small, reviewed backfills: `20261002110732_ChargeFullEsvForRegistrationMonth`
  (writes an audit snapshot), `20261002210650_AddBackfillStartedAt`, `20261001171537_AddEmailChannel`. The `DELETE`
  in `AddIncidentToSentReminders` is in `Down` only.
- Indexes cover every owner-scoped list and every idempotency key: `Transactions(UserId, ValueDate)`,
  `(BankAccountId, ExternalId)` unique on transactions, payments, candidates and foreign debits; invoice numbering
  is a unique partial index; `AuditLog(UserId, Entity, EntityId, At)`.
- `UpgradeFromPreviousReleaseTests` (`PreviousRelease = 20261001171537_AddEmailChannel`, `:23`) migrates across
  every row-rewriting migration after it, and the pre-migration dump backs this up in production.

### F4 (Medium). The backup schema version chain is too expensive for what it buys

- `Features/Backup/BackupDocument.cs:39-50` is at `CurrentSchemaVersion = 18`. All 17 bumps happened between
  2026-09-28 (go-live) and 2026-10-03. `BackupDocument.Upgrade.cs` is 319 lines with 17 steps, and there are 17
  fixtures, `api/tests/TaxesUa.Api.Tests/Features/Backup/Fixtures/backup-v02..v18.json`.
- Every owner-facing field now costs five edits: entity, `*Backup` record, validation, an upgrade step, and a new
  fixture.
- Most steps only write a default (`null`, `[]`, `""`): v3, v5, v6, v7, v8, v9, v10, v12, v13 at
  `BackupDocument.Upgrade.cs:98-260`. They exist because the restore enforces `RespectRequiredConstructorParameters`
  (`Program.cs:127`), and the backup inherits those options (`BackupEndpoints.cs:44`).
- Is the cost justified? The JSON backup is now one of four copies: the Coolify `pg_dumpall`, the nightly encrypted
  sidecar dump with a weekly restore check, the pre-migration dump, and this file. Its unique value is an
  owner-held, portable copy. That is worth keeping, but not at one schema version a day.
- Minimal fix, deletion-biased:
  1. Give new optional members of the `*Backup` records a default value in the positional constructor. System.Text.Json
     treats a parameter with a default as not required. An additive field then needs no version bump, no upgrade step
     and no new fixture.
  2. Ask the owner which backup files exist on disk. Set a support floor at the oldest of them, and delete the
     upgrade steps and fixtures below it. Steps that carry semantics stay: v1 `reviewStatus = Confirmed`,
     v14 `confirmedAt = linkedAt`, v16/v17.

### F6 (Medium). Tests run on PostgreSQL 16, production runs 18

- `api/tests/TaxesUa.Api.Tests/ApiFixture.cs:43` (`postgres:16-alpine`), `.github/workflows/ci.yml:78`,
  `docker-compose.local.yml:39` and `README.md:38` all use 16. Production is 18 (`docs/deploy.md:19`,
  `api/Dockerfile:20`, `deploy/backup/Dockerfile:7`, ADR-031). `graph/dependencies.md:97` still says 16, and
  `docs/architecture.md:39` says "16+".
- Why it matters: migrations, the upgrade test and every API test prove behaviour on a major version that production
  does not run. The deploy smoke test (`deploy/smoke.compose.yml:19`) is the only place 18 is exercised.
- Minimal fix: change those four `16` strings to `18`, and update the two docs.

## 6. Web architecture: holds

- Layering is enforced by `web/eslint.config.mjs:22-46`: app imports nothing above it, features do not import each
  other, data does not import features, shared imports nothing above it. There are no cross-layer relative imports
  (the only `../../` imports are the test harness reading `messages/*.json`).
- Types: every exported type in `web/src/data` is derived from `components["schemas"]` or an indexed access on one
  (`data/dashboard/useDashboard.ts:10`, `data/treasury/useTreasuryAccounts.ts:10`, …). No feature re-declares an
  API enum union; the literals appear only in `schema.d.ts`. CI checks `schema.d.ts` against the API.
- Data fetching: TanStack Query with hierarchical keys. `dashboardQueryKey` and `declarationsQueryKey` are nested
  under `periodsQueryKey` (`data/dashboard/useDashboard.ts:15`, `data/declarations/useDeclarations.ts:22`), so a
  ledger mutation invalidating `periods` refreshes the derived screens. Per-resource `useInvalidate*` helpers
  (`data/transactions/useTransactions.ts:34-37`) keep invalidation in one place per resource.
- One feature-level raw `fetch` (`features/auth/hooks/usePasskeyCeremony.ts:79-100`) still uses `readProblem`, so
  error codes survive. This is acceptable for the WebAuthn ceremony.
- No web source file is over 500 lines. The largest is `features/invoices/components/DraftEditor.tsx` (492).

## 7. Docs drift (F8, Low)

Ten claims checked.

TRUE:
1. `docs/architecture.md:36`: TanStack Table and Form are not used.
2. `:61`: reminders run every 5 minutes (`ReminderWorker.cs:13`).
3. `:191-200`: the boundary test exists, with cycle check, allow-list and doc table (except the shared-code clause, see below).
4. `:223-224`: a fixture exists for every backup version 2-18.
5. `:345-349`: `CrossSiteGuard` implements Sec-Fetch-Site, then Origin.
6. `:360-361`: the pre-migration dump refuses to migrate when the dump fails.
7. `graph/dependencies.md:13`: CI checks the schema.
8. ADR statuses are now "Accepted" (`docs/decisions.md`, ADR-001..031).

FALSE or stale:
- `docs/architecture.md:193-196` and `graph/dependencies.md:19-21`: "shared code … reaches no feature". False (F2).
- `docs/architecture.md:205-206`: the audit log snapshots "Transaction, BudgetPayment, Settings and TaxYearConfig".
  The interceptor audits 12 types (`Features/Audit/AuditSaveChangesInterceptor.cs:37-48`): Client, Invoice,
  TreasuryAccount, InvoicingDetails, DeclarationDetails, DeclarationFiling and NotificationChannel too.
- `docs/architecture.md:259-260`: the web feature list omits `audit`, `declaration` and `invoices` (`web/src/features/`).
- `graph/architecture.md:27`: "`BackupDocument.cs` is 1,800 lines (#186)". It is now 105. `:39-40` still lists #186 as
  a pending improvement.
- `graph/dependencies.md:97` and `docs/architecture.md:39`: PostgreSQL 16. Production is 18 (F6).
- `docs/TODO.md` at the commit d758079 still lists "#186 Split the largest API files" under Planned, and
  `plans/current.md` says #186 is "the one left". The working tree has an uncommitted fix for both (`git diff`
  shows it). Someone needs to commit it. Structural note: TODO.md duplicates GitHub issue state and goes stale on
  every merge. Consider cutting it to the open owner questions plus a link to the tracker.

## 8. Complexity hotspots and where deletion pays

Largest hand-written files (the `SourceSizeTests` gate is 600 lines per file):

| File | Lines | Branch tokens | Note |
|---|---|---|---|
| `Features/Declarations/DeclarationsEndpoints.cs` | 595 | 63 | trips the gate on the next edit |
| `Features/Settings/InvoicingEndpoints.cs` | 577 | 42 | validation and IBAN helpers can move to an `InvoicingRules.cs` |
| `Features/Notifications/NotificationsEndpoints.cs` | 560 | 44 | |
| `Features/Monobank/MonobankEndpoints.cs` | 524 | 43 | |
| `Features/Payments/TreasuryAccountsEndpoints.cs` | 520 | 72 | most branch-dense file in the API |
| `Features/Monobank/MonobankStatementImport.cs` (+ `.Pairing` 173) | 482 | 41 | cohesive pipeline |
| `Features/Backup/*` | 3,080 total | | F4 |
| `Features/Backup/PrototypeFile.cs` + `ImportEndpoints.cs` | 734 | 60 in PrototypeFile | F5 |

### F9 (Low). The file gate measures files, not types

`Architecture/SourceSizeTests.cs:21-32` counts lines per file and suggests "a partial class" as a valid split. The
transaction endpoints class is still 854 lines across four partials, and the invoices class is 839. That is fine as
navigation, but it is not a reduction. When `DeclarationsEndpoints.cs` trips the gate, split by moving logic into a
cohesive type, as `DpsXml.cs`, `CabinetForm.cs` and `DeclarationReadiness.cs` already are. Do not split it by
partial-classing the handler list.

### F5 (Medium). The prototype importer is the best deletion candidate

- `Features/Backup/PrototypeFile.cs:3-7` says: "No sample of that export exists in the repository, so this shape is
  derived from the ticket and the spec." The importer plus `ImportEndpoints.cs` is 734 lines. Add
  `ImportEndpointsTests.cs` (446 lines), `web/src/features/backup/components/PrototypeImportPanel.tsx`, the
  `data/backup` mutation, and an OpenAPI path.
- It is a one-time migration from a format never seen. The app has held real data since 2026-09-28. Either the owner
  already imported (then the code is dead), or the importer has never met a real file (then it is unproven).
- Minimal fix: ask the owner. If the prototype data is already in, delete the importer, its route, the panel, the
  tests and the Backup -> Transactions/Payments uses that only it needs. Then re-run the boundary test, because
  `Every_listed_edge_is_still_used` will name the edges to drop.

Other deletion candidates (Low):
- `NbuQr.MaxBytes` (`Engine/NbuQr.cs:24`) is referenced only by a test.
- The upgrade steps below a support floor (F4).

## F10 (Low). Hygiene

- `.claude/worktrees/` holds 31 stale agent worktrees using 21 GB (`git worktree list` shows 32 entries). They are
  excluded from git through `.git/info/exclude`, but they slow every unscoped `find` and `grep`. Prune the merged
  ones with `git worktree remove`.
- `reserve-card-ru-375-surplus.png` is still untracked in the repo root. It was flagged on 2026-10-02.

## What holds (no action)

- Engine purity, integer money and parameters only from the config, now partly enforced by test (ClockTests).
- The feature DAG: 82 edges, no cycles, folder and namespace parity, allow-list kept in sync with the graph doc by test.
- A single `OwnerLock`, a single `Problems` factory, stable error codes end to end, no English parsing in the web.
- A safe migration path: dump-or-refuse, CI waits for the new SHA, the previous-release upgrade test.
- Sync health alerts, an off-host encrypted backup with a weekly restore check, compose limits and log rotation.
- Web layering enforced by eslint, types generated from OpenAPI and checked in CI, hierarchical query keys.
- The 600-line gate on API sources. The largest web file is 492 lines.


---

# Backend bug hunt: taxes-ua at main d758079 (2026-10-05)

Scope: `api/src/TaxesUa.Engine`, `api/src/TaxesUa.Api`, engine tests. Read-only. Engine tests run green
(441 passed). API tests were not run (they need Postgres). Findings 1 and 3 were reproduced with a scratch
console that references the engine project (outside the repo). The other findings come from reading the
code path end to end.

## Findings

| # | Severity | Area | Status |
|---|---|---|---|
| 1 | High | Dashboard returns 500 when the year's group 3 income is negative | Verified (engine probe + code path) |
| 2 | Medium | monobank sync permanently drops a receipt whose NBU rate failed, once the window is older than 31 days | Verified by reading |
| 3 | Low | Limit `Warn` fires up to ~504 UAH before 85% of the real 2026 limit | Verified (engine probe) |
| 4 | Low | `POST /transactions` and `POST /payments` skip the owner lock | Verified by reading; impact PLAUSIBLE |
| 5 | Low | Residual #173 gap: a learned military levy account with no end is offered without warning in 2027 | By design per Rule 16; residual risk |

### 1. High: the dashboard throws when the last group 3 quarter's cumulative income is negative

- `api/src/TaxesUa.Api/Features/Dashboard/DashboardEndpoints.cs:97-99` passes
  `last.Income.CumulativeIncomeKop` of the year's last group 3 quarter into `LimitMonitor.Evaluate`.
- `api/src/TaxesUa.Engine/LimitMonitor.cs:36` does `ArgumentOutOfRangeException.ThrowIfNegative(incomeKop)`.
- Rule 1 allows negative period income: a refund lowers the income of the period it happens in. The
  engine expects this case; it emits `EngineWarning.NegativeCumulativeTax` (`Accruals.cs:278-281`).
- Input: a 2027 tax year is configured. In January 2027 the owner records a `RefundToClient` of
  1,000.00 UAH linked to a December 2026 receipt. No 2027 income yet.
- Result: every 2027 quarter is group 3 with cumulative income −100000. The last one is Q4.
  `Evaluate(-100000, …)` throws, so `GET /api/dashboard` answers 500 until enough 2027 income arrives.
  The home screen is down for the whole period.
- Probe output: `Q4 cum 2027: -100000`, and `LimitMonitor.Evaluate(-100, cfg)` throws
  `ArgumentOutOfRangeException`.
- Minimal fix: clamp at the call site, `LimitMonitor.Evaluate(Math.Max(last.Income.CumulativeIncomeKop, 0), …)`.
  Or let `Evaluate` treat negative income as 0 (0%, `Ok`, remaining to the first threshold).
- Add an engine test and a dashboard test for a refund-only year.

### 2. Medium: a receipt whose NBU rate lookup failed is never retried once its window is older than 31 days

- `MonobankStatementImport.RecordAsync` (`Features/Monobank/MonobankStatementImport.cs:336-342`) answers
  `RecordTransactionResult.RateUnavailable` with `Outcome.Skipped` ("waits for its NBU rate").
  `Invalid` gets the same treatment.
- `ImportAsync` (`:298-306`) then moves `SyncedThrough` to `statement.To` anyway.
- The only retry path is the 31-day rewind at the next run: `from = Min(start, now - Window)` (`:73`). A
  skipped item is re-read only if it is younger than 31 days at the next sync.
- Concrete state: the owner follows a USD/EUR FOP account, which starts a backfill from the registration
  date (or from 1 January). Or the sync was stopped for more than 31 days: a token was rejected and
  replaced, or the server was down. NBU answers 5xx or times out (`NbuLookup.Unavailable`,
  `NbuRateClient.cs:51-62`) for a window older than 31 days.
- Result: the foreign receipt is never recorded. It is missing from income, so EP, VZ, the limit and the
  declaration are understated. Only a log line and `ImportBatch.SkippedCount` record it, and the UI shows
  that count only for the latest batch. Skipped holds share the same counter.
- Minimal fix: when any item of a window comes back `RateUnavailable`, do not advance `SyncedThrough` past
  it. Either end the walk without committing the cursor (record `SyncFailure`, as `FetchAsync` does for
  other failures), or keep the cursor at that item's time. The ExternalId dedup makes a re-read safe.
- `Invalid` is rarer: it is permanent by nature (e.g. amount over the bound), so logging may be acceptable
  there. But it should surface as a needs-attention item, not a silent skip.

### 3. Low: the 85% warning compares rounded basis points, so it fires early and RemainingKop jumps

- `api/src/TaxesUa.Engine/LimitMonitor.cs:39-47` computes `percentBp = ShareBp(income, limit)`. That is
  rounded half-up to a whole basis point. The level and the next threshold are then decided on the
  rounded value.
- One basis point of the 2026 limit (1,009,104,900 kop) is about 1,009 UAH. So any income from
  85% − ~504.55 UAH up to 85% − 0.01 rounds to 8500 bp. It reads as `Warn`, and `RemainingKop` switches
  from "left to 85%" to "left to the limit".
- Rule 4 says the opposite: "one kopeck under either line stays at the level below it".
- Probe: income 857,739,164 kop (85% − 1 kop) gives `pct 8500 level Warn remaining 151365736`. It should
  be `Ok`, remaining 1.
- The tests miss it because `LimitMonitorTests` uses a round 10,000,000 kop limit, where thresholds are
  exact.
- The `Exceeded` level is correct: it compares kopecks.
- Minimal fix: compare in kopecks. Use `warned = thresholds.Any(pct => income * 100 >= limit * pct)`, and
  pick the next threshold the same way. Keep `PercentBp` for display only. Add a test with the real 2026
  limit.

### 4. Low: `POST /transactions` and `POST /payments` do not take the owner lock

- The lock's own comment (`Data/OwnerLock.cs`) says the owner's writes to rows that restore or sync change
  must take it.
- PUT, DELETE and confirm of transactions and payments take it (`TransactionsEndpoints.cs:155,219,283`,
  `PaymentsEndpoints.cs` PUT and DELETE). `TransactionRecorder.RecordAsync` (`POST /transactions`) and
  `POST /payments` save with no transaction and no lock.
- Consequences (PLAUSIBLE, since one owner rarely races themselves):
  - A POST during a restore can land between the restore's delete and its insert. The row survives the
    restore, or the save fails with an FK error on a client or receipt the restore just deleted.
  - Two refund POSTs, or a refund POST beside a `PUT` that lowers the receipt, can both pass the
    over-refund check (`TransactionsEndpoints.Links.cs:54-63`), which reads and writes outside a lock.
  - The candidate confirm's "a payment you recorded has the same date…" guard
    (`PaymentCandidatesEndpoints.cs:100-125`) is meant to catch a concurrently typed payment. A
    `POST /payments` racing it is not serialized, so both can record the same bank payment.
- Minimal fix: wrap both POSTs in `BeginTransactionAsync` + `OwnerLock.AcquireAsync`, after the NBU lookup,
  as PUT does.

### 5. Low: residual of #173

- Rule 16, as implemented, gives a Learned account no end until the owner sets one. The one-tap
  "2026-12-31" offer for the military levy account shows only while that day has not passed
  (`knowledge/business-rules.md:980-985`).
- If the owner never taps it, in February 2027 the Pay panel offers the learned 2026 temporary levy account
  for the Q4 2026 levy (due 2027-02-19) with no `expiry` (`PaymentDetailsEndpoints.cs:65-67`). That is the
  audit's original risk.
- The code matches the documented rule, so this is a product decision, not a code bug.
- Option: from 2027-01-01, flag a MilitaryLevy account learned from a payment dated 2026-07-01..2026-12-31
  that has no end, without setting one.

## Fix verification (39ce9bd..HEAD)

- **#171, full ESV for the registration month.** Correct.
  - `EsvRegistrationMonthPolicy.FullMonth` is first in the enum and the entity default.
  - `Accruals.RegistrationMonthBaseKop` charges the full minimum wage. `Prorated` prorates the base once,
    then applies the rate once.
  - The backup upgrade from v15 maps `Prorated` to `FullMonth` (`BackupDocument.Upgrade.cs`
    `UpgradeFromVersion15`).
- **#172, group 3 status.** Correct.
  - `Group3Start` = max(registration, `Group3Since`).
  - `IncomeLedger` excludes operations before it, transitively for refunds.
  - `Accruals.EndsBeforeGroup3` keeps ESV for those quarters and zeroes EP and VZ.
  - `Balances` routes EP/VZ payments naming those quarters to `OutsideGroup3`, and ESV payments stay in
    the pool.
  - The annex covers group 3 months only, `From` = `Group3Start`.
  - Reminders, the calendar and the declaration skip non-group-3 quarters.
  - `DpsStatusEndpoints.Validate` allows only the registration date or a later quarter start.
    `SettingsEndpoints` PUT drops a stale `Group3Since`.
  - `Group3Application.Pending` uses `Group3Start`, so it survives a registration date edit.
- **#173, Treasury account end.** Correct as specified.
  - Expired withholds the recipient and the QR.
  - `ExpiresBeforeDue` compares with the kind's shifted due date.
  - The end follows its IBAN through relearn.
  - Residual risk: finding 5.
- **#174/#199, sync health.** Correct.
  - Progress is the caught-up cursor, or the latest batch after `BackfillStartedAt`, or that start.
  - Incident keys move with recovery and are suppressed while sync work is pending.
  - The migration seeds `BackfillStartedAt` from `CreatedAt`.
- **#178, new tax year.** Correct.
  - December only, keyed by year, sent from 09:00 Kyiv.
  - The missing current year is a separate incident.
  - `ReminderSender` falls back to last year's view in January.
- **#189, mark paid.** Correct.
  - `PaidOn` may not be after Kyiv today, the amount must be above 0, and an early date is allowed.
  - Backup import and candidates pass no `today`.
- **#222/#225/#228, declaration XML.** Correct.
  - The header carries HNAME/HLOC/HEMAIL/HTEL/HTIN, T1RXXXXG1S/G2S with official KVED names (unknown codes
    block readiness), and HFILL/D_FILL = Kyiv today at generation.
  - HBOS is given name plus SURNAME.
  - The file name follows standard 729 (state, type 00, count 0000001, period type, month, year, office).
  - The annex row element names (`R09{m}G{c}`) match `F0133109.xsd`, including R0910..R0912.
  - The body is schema-validated before it is stored.

## Areas checked and found correct

- **Kopeck arithmetic.**
  - `Money.DivRoundHalfUp` rounds half away from zero, not banker's, and is used everywhere.
  - Each accrual is a single rounding of a cumulative figure (`SingleTaxOn`, levy on cumulative income), so
    months add up to quarters and quarters to the year exactly.
  - Declaration lines are read off the same cumulatives: line 11 = 12 − 09, 13 = 12 − 14.1,
    24 = 23 − 25. No sum-of-rounded drift.
  - The excess tax applies to the excess only, rounded once. The per-receipt set-aside rounds per receipt,
    which is documented.
- **Rule 2 conversion.** `ToUahKop(amountMinor × RateE4 / 10^4)` rounds half-up. The rate is frozen on
  edit unless the currency or date changes. The UAH bound check uses Int128.
- **Deadlines.**
  - ESV is due on the 19th, the declaration at quarter end + 40, the tax at the statutory declaration date
    + 10, with shifting matching the 2026 table.
  - Q4 falls into next January/February.
  - `NextBusinessDay` has a scan limit.
  - Tax year config days are bounded at ≤ 28.
- **Kyiv time.**
  - All "today" values come from `KyivTime.TodayInKyiv`.
  - Bank times are converted with `KyivDate()`. `KyivMidnight` is unambiguous: DST switches at 03:00/04:00.
  - ICS events are all-day with relative alarms.
- **Balances (Rule 7).**
  - Payments settle the oldest due first, across years.
  - A negative quarter's accrual becomes credit.
  - Payments past the horizon are left out, and duplicate years are rejected.
- **Monthly advances.** They split quarter allocations oldest-first, and refund months credit their
  quarter's other months. The reminder and NextStep advance logic is consistent with them.
- **Reminders.**
  - Only the latest due offset is sent, and the day-after reminder only on that day.
  - Dedup goes through a covered-kinds union plus a unique index on (user, date, kinds, offset, channel).
  - A transient failure releases the claim, and the context is detached on a unique-violation race.
- **Refund links.** The receipt must be the owner's Income row in the same currency and not over-refunded.
  A receipt with refunds is locked to Income and its currency, and cannot be deleted.
- **Imported row delete.** The tombstone keeps its ExternalId and drops its links. The query filter hides
  it from income.
- **FX sale pairing.**
  - A tolerance-closed load range with Kuhn matching.
  - It moves only unreviewed suggestions, and never moves a receipt with linked refunds to FxSale.
- **Backup.**
  - The upgrade chain covers v1→18.
  - Restore runs under the owner lock and deletes then inserts in one transaction.
  - KVED validation is relaxed on restore.
- **Prototype import.** It parses amounts as text with integer scaling, and checks that uah matches
  Rule 2's formula.
- **EF and Postgres.** The FxRates cache insert race is handled. The rows are detached, and EF's automatic
  savepoint keeps an enclosing transaction usable.
- **Enum binding.** The strict JSON enum converter rejects numbers and unknown names.


---

# web/ bug hunt at main d758079 (2026-10-05)

Scope: `web/src`, `web/service-worker`, `serwist.config.mjs`, `next.config.ts`, `messages/`. This was a read-only review. Nothing in the repo was edited.

The review was split three ways:
- I reviewed the dashboard, declaration and Cabinet view, auth, shell, i18n, the service worker and PWA, CSP, the shared UI, and the money and date helpers.
- One subagent reviewed settings and backup.
- A second subagent reviewed transactions, invoices, payments, periods and audit.

I re-read the code behind every subagent finding marked "verified" below. Anything not proven end to end is marked PLAUSIBLE.

## Checks run
- `pnpm test`: 52 files, 594 tests, all passed.
- `pnpm lint`: clean.
- `pnpm exec tsc --noEmit`: one error, `.next/types/validator.ts(42,39): Cannot find module '../../src/app/page.js'`. This is a stale local `.next/` build output, not a source bug. The file `.next/server/app/index.html` comes from an old layout. `tsconfig.json` includes `.next/types/**`, so the leftover build breaks a local typecheck. Fix: `rm -rf web/.next` or rebuild. CI is not affected because it builds from clean.

## Previous audit fixes: do they hold?

| Audit item | Status | Evidence |
|---|---|---|
| #181 error codes | Holds | `data/api/client.ts:44-66` reads `code` and `errorCodes`. `useApiErrorText.ts:19-35` translates by code with an `unknown` fallback. All 158 snake_case codes in `api/src/TaxesUa.Api/ProblemCodes.cs` have an `apiErrors.*` key. uk and ru have identical key sets and no placeholder mismatches (checked by script). |
| #187 a11y forms | Holds | `shared/ui/fields.tsx:64-105`: label `htmlFor`, `aria-invalid`, `aria-describedby` (hint and error), and an error region with `role="alert"` that goes quiet under a summary (`FieldForm`). The pay panel shows its error only after a touch (`pay-panel.tsx:115,133`). |
| #188 dashboard calm, retry | Holds | `DashboardScreen.tsx:30-48` shows one banner above the hero and folds the rest into a `<details>`. `notices.ts` ranks them. `data/api/LoadState.tsx` adds `role=status`/`alert` and a retry that keeps focus. AuthGate shows a spinner, not `null`. |
| #189 mark paid | Holds | `HeroCard.tsx:110-254` has a date field (min 2000-01-01, max today) and per-kind amounts seeded from the pay panel. A partial failure keeps only the failed kinds in the form for a retry. |
| #190 PWA | Holds | `service-worker/sw.ts`: `skipWaiting:false`, navigations `NetworkOnly`, `/offline.html` fallback, and no runtime cache for `/api`. `UpdatePrompt.tsx` shows a prompt, then SKIP_WAITING, then a reload, and rechecks on visibility and hourly. The manifest has `id` and separate `any`/`maskable` entries. |
| Audit #11 (year onboarding) | Partly holds | `NewTaxYearNotice` deep-links to `?tab=taxYears`, but the `MissingTaxYear` state card still uses `SettingsLink`, which goes to `/settings` and opens the `fop` tab (`DashboardScreen.tsx:161-165,299-307`). Low. |

## Findings

### Medium

**M1. Signing out or in with a passkey leaves the previous account's data in the query cache.** Verified.
- Where: `data/auth/useSignOut.ts:14-17` and `features/auth/hooks/usePasskeyCeremony.ts:135-139`. Both remove only `meQueryKey`, then navigate client-side. The QueryClient is a browser singleton (`data/QueryProvider.tsx:12-22`), so the dashboard, transactions, payments and other queries survive.
- Scenario: A signs out. B (also on `Auth__AllowedEmails`; the data is per-UserId per `knowledge/domain-model.md`) signs in with a passkey in the same tab within 60 s. The dashboard renders A's cached data with no refetch, because `staleTime` is 60 s. Up to the 5-minute gcTime, it shows A's data, then swaps to B's.
- Google sign-in is a full-page redirect, so it is not affected.
- Fix: `queryClient.clear()` in both `onSuccess` handlers.

**M2. A failed background refetch unmounts forms that hold unsaved edits.** Verified.
- Where: guards of the form `if (query.isError || !data) return <LoadState/>`:
  - `FopSettingsForm.tsx:93`
  - `DpsStatusSection.tsx:90`
  - `TaxYearTable.tsx:89`
  - `InvoicingForm.tsx:39`
  - `DeclarationDetailsForm.tsx:55`
  - `ClientsSection.tsx:73`
  - `TreasuryAccountsSection.tsx:31`
  - `MonobankConnectionSection.tsx:40`
  - `NotificationsSection.tsx:35`
  - `invoices/components/InvoicesScreen.tsx:35` (`OpenInvoice`, which hosts `DraftEditor`)
- Background: in TanStack v5, `isError` is true after a failed background refetch even though `data` is kept.
- Scenario: the owner edits a draft invoice or the invoicing details. After more than 60 s they switch tabs and come back. `refetchOnWindowFocus` refetches, and the request fails on a network blip or a 401. The `*Body` component that owns the `useState` form is replaced by "load failed", and the edits are gone.
- Fix: guard on `!data` only, for example `if (query.isLoading || !data)`. If the error should be visible while data is on screen, render `LoadState quiet` beside the form.

**M3. Finishing a monobank sync does not refresh the dashboard or periods.** Verified.
- Where: `data/monobank/useMonobank.ts:25-28`. When the poll sees `syncPending` clear, it invalidates `["transactions"]` and `["clients"]` only. The dashboard and declarations are under `["periods"]`, a separate root.
- Scenario: the owner syncs and new income arrives. The next step, the limit and the reserve on Home stay stale for up to 60 s.
- Fix: also invalidate `periodsQueryKey` there. Do the same in `useSaveMonobankToken`, so a replaced token clears the red "token rejected" banner (Low).

**M4. Invoice issue, cancel and delete do not refresh the dashboard's overdue-invoice notice.** Verified.
- Where: `data/invoices/useInvoices.ts:60-64`. `useInvalidateInvoices` invalidates `["invoices"]` only. `overdueInvoiceCount` comes from the dashboard (`DashboardScreen.tsx:64,100`).
- Fix: also invalidate `dashboardQueryKey`.

**M5. Settings forms never pick up new server data, so Save can overwrite a restored backup.** Verified by reading. Not run.
- Where: the forms seed state once with `useState(() => toFormState(server))`:
  - `FopSettingsForm.tsx:107`
  - `DpsStatusSection.tsx:103`
  - `InvoicingForm.tsx:52`
  - `DeclarationDetailsForm.tsx:69`
  - `TaxYearTable.tsx:205`
- `BackupPanel` renders on the same page below `SettingsTabs`, and restore runs `invalidateQueries()` on everything.
- Scenario: the FOP tab is open and the owner restores a backup. The query refetches the restored values, but the form still shows the old ones. Pressing Save writes the pre-restore values back.
- Fix: after a successful restore or import, remount the tabs, for example by bumping a key on `SettingsTabs`.

**M6. Save failures that are not an `ApiError` show nothing.** Verified for `FopSettingsForm.tsx:109`; the same pattern is in the files below.
- Where: `const failure = mutation.error instanceof ApiError ? … : null`, with the message rendered only when `failure` is set. Files:
  - `FopSettingsForm`
  - `DeclarationDetailsForm:72`
  - `InvoicingForm:55`
  - `DpsStatusSection:111`
  - `TaxYearTable`
  - `MonobankConnectionSection`
  - `InvoicingSignature:25`
  - `InvoicingMonobankPrefill:23`
- Scenario: the connection drops, so openapi-fetch rejects with a `TypeError`. The button re-enables, no message appears, and the owner may assume the save worked.
- Same class in auth: `PasskeyButton.tsx:39-46` renders only `PasskeyCancelledError` and `ApiError`. An `InvalidStateError` (passkey already registered on this authenticator) or a network `TypeError` shows nothing.
- Fix: key the message on `mutation.isError`, and use `apiText.withReason(prefix, error)`, which already falls back to the prefix.

**M7. The Settings locale, theme and default currency are saved but nothing in the UI reads them.** Verified that `useSettings()` has exactly one caller, `FopSettingsForm.tsx:90`.
- The UI language comes from the cookie (`i18n/setLocale.ts`), and the theme comes from `next-themes` storage.
- Scenario: the owner picks "Русский" or "Dark" in Settings and saves. The UI does not change.
- ADR text (decisions.md ~801) says the settings locale drives the Telegram reminder language. If that is the intent, relabel the fields ("Language of reminders"). Otherwise, apply the values to the cookie and theme on save.

### Low

**L1. Declaration screen state leaks between quarters.** Verified.
- Where: `DeclarationScreen.tsx:53`. `<Declaration>` is not keyed by period. When moving to a quarter that is already cached, `data` is defined right away, so `XmlFile`'s `chosen` type, a `generate` error alert ("conflict"/"failed"), and `MarkForm`'s `filedOn` or error carry over from the previous quarter.
- Fix: `<Declaration key={`${period.year}-${period.quarter}`} …/>`.

**L2. The mark-paid failure drops the API reason.** Verified.
- Where: `data/payments/usePayments.ts:91-97` catches every error into `failed: PaymentKind[]`. `HeroCard.tsx:245-251` then shows only "could not record X".
- Scenario: a validation code such as a date before registration never reaches the owner.
- Fix: keep `{kind, error}` and append `apiText.withReason`.

**L3. The pay panel hides the recipient details when only the amount query fails.** PLAUSIBLE.
- Where: `usePaymentDetails.ts:83-84` ORs the two queries' `isError`, and `pay-panel.tsx:145` then replaces all details (IBAN, name, code, purpose) with "failed + retry".
- Scenario: an amount the API rejects (for example over a server maximum) hides the copy fields that do not depend on the amount. The retry then repeats the same failing request.
- Fix: treat a `withAmount` failure as a QR-only problem inside `QrBlock`.

**L4. No global 401 handling.** Verified.
- Where: only `AuthGate` reacts to 401, and only on `/api/auth/me`.
- Scenario: the session expires mid-use. Data screens show "load failed" and saves show a generic error until a focus refetch of `me` (after 60 s staleness) redirects to the login page.
- Fix: a `QueryCache`/`MutationCache` `onError` that, on `ApiError.status === 401`, invalidates `meQueryKey`.

**L5. Settings tab links to the URL already open do nothing.** Verified.
- Where: `settings/page.tsx` keys `SettingsTabs` on `?tab`. The scenario: open `/settings?tab=fop`, click the DPS tab, then click "set registration date" (`DpsStatusSection.tsx:155`, linking to `/settings?tab=fop`). The URL and key are unchanged, so the tab stays on DPS.
- Fix: sync the tab with `router.replace(?tab=)` on change.

**L6. The tax-year "verified" date uses the browser time zone.** Verified.
- Where: `TaxYearTable.tsx:392`.
- Fix: `formatInstantInKyiv` or `timeZone: "Europe/Kyiv"`.

**L7. Audit field labels are missing and the history entity allowlist is incomplete.** Verified.
- `audit.fields.manualUpdatedAt` and `audit.fields.learnedAt` are absent in both locales, although `features/audit/fields.ts:113,120` lists them. History shows the raw key.
- `app/(app)/history/page.tsx:7-17` omits `Backup`, `TreasuryAccount` and `NotificationChannel`, all of which are in the generated `AuditedEntity`. For example, `?entity=TreasuryAccount` falls back to the unfiltered log.

**L8. A client rename does not invalidate invoices.**
- Where: `data/clients/useClients.ts:22-29`. Invoice list and detail show `clientName`, which stays stale for 60 s.

**L9. A deleted draft refetches the deleted invoice before going back.** PLAUSIBLE.
- Where: `useInvoices.ts` `useDeleteInvoice`. `onSuccess` returns the `invalidateQueries` promise, so the mounted detail query refetches, gets a 404 and retries once. `onBack` is delayed by about 1 s, with a brief load-failed flash.
- Fix: `removeQueries` for the detail key before invalidating.

**L10. Editing a refund may display "None" while the form submits the receipt id.** PLAUSIBLE.
- Where: `TransactionForm.tsx:79,355-363`. This happens when the referenced receipt is not among the loaded options (still loading, failed, or excluded by the server).
- Fix: add an option built from `editing.refundsReceipt` when it is missing.

**L11. A stale `editing` row on Transactions.**
- The year switch does not clear `editing`. Deleting the row being edited leaves the form doing a PUT on a deleted id, which returns 404 and "save failed".

**L12. Prerendered HTML could be precached.** PLAUSIBLE, latent.
- Where: `serwist.config.mjs` leaves `precachePrerendered` at its default `true`, so the preset adds `.next/server/app/**/*.html` to the precache (`node_modules/@serwist/next/dist/index.config.mjs:36`). A precached URL is served from the precache, ahead of the `NetworkOnly` navigation rule.
- Today every page is dynamic (the root layout reads `headers()`), so nothing qualifies. A future static route would ship stale HTML until the worker updates, which breaks the "pages always go to the network" promise in `sw.ts`.
- Fix: `precachePrerendered: false`.

**L13. `formatPlainAmount` is wrong for negative values.** Latent.
- Where: `shared/lib/money.ts:96-101`. `-150` formats as `"-2.-50"`.
- All current callers pass a value greater than 0 (`pay-panel.tsx:111,207`, `HeroCard.tsx:147`).
- Fix: format the absolute value and prefix the sign, or document and assert `>= 0`.

**L14. Smaller items.** All verified.
- `ReserveJarSection.tsx:29`: a failed `choose` error keeps showing after a later successful refresh.
- `InvoicingSignature`: a `remove` error persists through later uploads.
- `TreasuryValidUntil.tsx:23`: the error survives Cancel and reopen.
- Telegram link polling (`useNotificationChannels`) runs every 3 s forever after `link.expiresAt`, with no "expired" message.
- `NumberField`/`MoneyField` in `fields.tsx` cannot be emptied: `numberOrZero` turns "" into 0, so clearing and typing 5 gives "05". PLAUSIBLE.
- `ClientsSection` "Edit" buttons have no client name in their accessible name.
- `useFxRate.ts:25` has `staleTime: Infinity`, so a fallback NBU rate never refreshes in the session (display only).
- Duplicated `kopecksToAmountText` in `TransactionForm` and `PaymentForm`. `DynamicTranslator` is redefined three times (`useApiErrorText.ts`, `invoices/errorMessages.ts`, `HistoryPanel.tsx`).
- `pay-panel.tsx:29-40` hand-writes `PayDetails`, including the `"ExpiresBeforeDue" | "Expired"` literal, instead of deriving it from `PaymentDetailsResponse`.
- Cross-boundary: a non-allowlisted Google account lands on raw English ProblemDetails JSON (`api/.../AuthEndpoints.cs:85-91` returns a problem from a browser navigation), so the translated `account_not_allowed` text is never shown. Redirecting to `/login?error=<code>` and rendering it would fix this.

## Checked and found fine
- **Money:**
  - `parseHryvnia` handles "1 902,34", NBSP and U+202F (bytes checked), `.` or `,` as the decimal separator, rejects "1,902.34", three decimals and negatives, and builds kopecks from digit strings.
  - `parseRate`, `toUahKop` (BigInt, half away from zero) and the invoice line math (BigInt, half-up) are correct.
  - `formatMoney`/`formatMinor` use the active locale.
- **Dates:**
  - `todayInKyiv` is used for defaults and `max` attributes.
  - `parseDateOnly` builds dates from parts.
  - `quarterEndOf`, `dayAfterQuarter` and `addDays` use UTC anchors.
  - Instants go through `formatInstantInKyiv`. The only gap is L6.
- **Dashboard:**
  - Debts are unique per kind (`api/.../NextStep.cs:67-83`), so `key={debt.kind}` and `failed.includes(kind)` are safe.
  - The `JarCover` shortfall/`topUpBy` pairing matches the engine contract.
  - The notice ranking is sound.
- **Fill in the Cabinet:**
  - Copy writes the server's `field.value` verbatim.
  - The copy buttons appear only when `readiness.ready`.
  - Failure feedback is a live `role=status`.
  - Keys are unique per element, row, month and column.
  - Month names are UTC-anchored.
- **Service worker:**
  - `/api` is never cached.
  - Navigations are NetworkOnly with an offline fallback.
  - The update waits for consent.
  - Listeners attach before the async `register()` resolves, and an already-waiting worker still fires `waiting`.
- **Security:**
  - No `dangerouslySetInnerHTML` or `innerHTML` in `src`.
  - The nonce CSP with `strict-dynamic` is set per request.
  - The static CSP covers file paths.
  - Google sign-in is a link, so `form-action` is not involved.
- **i18n:**
  - uk and ru keys and placeholders match.
  - No hardcoded Cyrillic, `aria-label` or `placeholder` literals.
  - The Ukrainian-only `declaration.figures` in ru is deliberate (official line names).
- **Phone layout:** the only `data-scroll-strip` (`settings-tabs`) is listed in `e2e/layout.spec.ts` `scrollingStrips`.
- **Invalidation elsewhere:**
  - Transactions invalidate transactions, clients, periods and invoices.
  - Payments invalidate payments, periods and treasury.
  - Settings, DPS and tax-year changes invalidate periods and more.
  - The reserve-jar hooks invalidate the dashboard.
  - Restore and import invalidate everything.
- **Types:** data hooks alias `components["schemas"][…]`, and no other hand-written type duplicates the schema apart from L14.


---

