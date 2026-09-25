# Spec: FOP Group 3 Tax Tracker MVP (Stage 1)

Status: draft, published to the tracker. Tickets: [tickets.md](tickets.md).
Glossary: [knowledge/glossary.md](../knowledge/glossary.md). Rules:
[knowledge/business-rules.md](../knowledge/business-rules.md).
ADRs: [docs/decisions.md](../docs/decisions.md).

## Problem Statement

I am planning to open a Group 3 FOP and work for foreign clients, getting paid in USD and EUR.
Every quarter I need to know how much income in hryvnia I received, at the NBU rate on the credit
date, how much EP, VZ and ESV is accrued, what to pay and file and by when, how much is already
paid, and whether there is an overpayment. Right now this is computed in a prototype with manual
rate entry and "paid" checkmarks, with no real payment ledger. A wrong date or amount costs a
penalty, and reconciling against the Electronic Cabinet requires manual arithmetic.

## Solution

A web app installable on a phone, where I enter receipts and budget payments, and it pulls the
NBU rate, computes the accruals from the year's parameters, keeps a balance per payment kind,
shows the nearest next step with its date and amount, watches the income limit, and prepares the
declaration numbers cumulatively. All data can be exported and restored from a backup. The app
does not pay taxes and does not file declarations.

## User Stories

Receipts

1. As a FOP, I want to enter a receipt with a credit date, amount and currency, so it lands in
   the right period's income.
2. As a FOP, I want the NBU rate to be filled in automatically for the credit date, so I don't
   have to look it up manually.
3. As a FOP, I want to see that a weekend's rate came from the last business day, and which day,
   so I understand where the number came from.
4. As a FOP, I want to correct the rate manually, so it matches the bank statement when they
   disagree.
5. As a FOP, I want to see the hryvnia equivalent before saving, so I can eyeball the amount.
6. As a FOP, I want the rate and hryvnia amount to be fixed at save time and never change later,
   so the declaration doesn't "drift".
7. As a FOP, I want to specify an operation type — income, refund to client, own transfer,
   currency sale, own deposit, erroneous refund, other — so only the right entries count as
   income.
8. As a FOP, I want a mandatory reason for a "not income" entry, so a year later I understand why
   it was excluded.
9. As a FOP, I want to link a receipt to a client and an invoice number, so I can find operations
   by counterparty.
10. As a FOP, I want to edit and delete a receipt with a confirmation, so I can fix mistakes
    without risking an accidental delete.
11. As a FOP, I want to see the year's list of receipts with a hryvnia total, so I can reconcile
    it against the bank.
12. As a FOP, I want a warning when entering an operation dated before the FOP registration date,
    so I don't count personal income as business income.
13. As a FOP, I want a refund of a client prepayment to reduce the income of the period in which
    the refund happened, so the calculation follows the rules.

Budget payments

14. As a FOP, I want to record an actual payment with a date, kind (EP, VZ, ESV), amount and
    period, so the app knows what's paid.
15. As a FOP, I want to see, per kind, accrued, paid and remaining separately, so it matches the
    integrated ledger card.
16. As a FOP, I want an overpayment of a kind to carry forward to the next period of that kind,
    so I don't overpay.
17. As a FOP, I want an EP overpayment to never offset an ESV debt, so balances match the actual
    treasury accounts.
18. As a FOP, I want to record a monthly advance, so it credits against the quarterly obligation.
19. As a FOP, I want to edit and delete a payment, so I can fix typos.

Home screen

20. As a FOP, I want to see the single nearest unfinished step: what, by when, how much, how many
    days remain, so I don't have to hold deadlines in my head.
21. As a FOP, I want to see an overdue item in red with the number of days, so I immediately know
    what's urgent.
22. As a FOP, I want to see the year's income against the limit with a percentage, so I see the
    threshold coming in advance.
23. As a FOP, I want a warning at 85% and 100% of the limit, so I can prepare to switch systems in
    time.
24. As a FOP, I want to see the tax burden as a percentage of income, so I understand the real
    rate.
25. As a FOP, I want to see "all done" when there are no obligations, so I don't go looking for
    hidden tasks.
26. As a FOP, I want a hint before the registration date that there are no obligations yet, so an
    empty screen doesn't worry me.
27. As a FOP, I want a warning if the current year's parameters aren't verified, so I don't
    compute against stale rates.

Periods and declaration

28. As a FOP, I want a quarterly table — income, EP, VZ, ESV, total, paid, remaining, deadlines —
    so I see the year's whole picture.
29. As a FOP, I want a monthly table with recommended advances in "monthly ahead" mode, so I can
    pay evenly.
30. As a FOP, I want to see the declaration numbers cumulatively for the quarter, half-year, nine
    months and year, so I can copy them into the Electronic Cabinet without recomputing.
31. As a FOP, I want to see shifted deadlines when the statutory date falls on a weekend, so I
    don't pay earlier or later than needed.
32. As a FOP, I want Q4 obligations to show next year's dates, so the January deadlines aren't
    lost across the year boundary.
33. As a FOP, I want to switch years, so I can look at past periods.

Settings

34. As a FOP, I want to set the FOP registration date, so ESV and obligations are computed from
    the right month.
35. As a FOP, I want to choose "quarterly" or "monthly ahead" payment mode, so the app adapts its
    hints.
36. As a FOP, I want to choose the ESV policy for the registration month (full amount or
    prorated), so I can follow my accountant's guidance.
37. As a FOP, I want to mark an ESV exemption, so the accrual zeroes out when an employer pays it
    for me.
38. As a FOP, I want to configure deadline-shifting rules — which days are weekends, the holiday
    list, whether the payment deadline is counted from the statutory declaration date, whether
    the payment deadline itself shifts — so I can adapt to changes in DPS practice.
39. As a FOP, I want to view and edit each year's parameters — minimum wage, rates, limit,
    deadline day counts — so a new year needs no code change.
40. As a FOP, I want to copy a year's parameters into the next year and mark them verified with a
    source link, so updating takes a minute.
41. As a FOP, I want to see that the monthly ESV amount and the hryvnia limit are recomputed
    automatically from the minimum wage, so I don't make a multiplication mistake.

Export and backup

42. As a FOP, I want to download the year's receipts as CSV and XLSX, so I can hand them to an
    accountant.
43. As a FOP, I want to download a full backup of all data as JSON, so I don't depend on the
    server.
44. As a FOP, I want to restore data from a backup with a confirmation, so I can migrate or roll
    back.
45. As a FOP, I want to import JSON from my current prototype, so I don't re-enter the history.
46. As a FOP, I want a repeat import to never create duplicates, so importing is safe.

Sign-in and security

47. As a FOP, I want to sign in via Google, so I don't have to keep yet another password.
48. As a FOP, I want to add a passkey and sign in with it from my phone, so sign-in is fast and
    secure.
49. As the owner, I want sign-in restricted to my own email, so no one else can register.
50. As a FOP, I want to sign out, so I can close access on someone else's device.
51. As a FOP, I want a change log for transactions, payments and settings, so I can see what I
    changed and when.
52. As a FOP, I want everything to run over HTTPS, so financial data never travels in the clear.

Shell

53. As a FOP, I want to install the app on my phone as a PWA, so I can open it like a regular
    app.
54. As a FOP, I want a light and dark theme following the system setting, with a manual toggle,
    so it's comfortable to look at.
55. As a FOP, I want the interface in Ukrainian by default with a switch to Russian, so I can use
    whichever language is convenient.
56. As a FOP, I want a disclaimer about the informational nature of the calculation, so I
    remember to reconcile with the Cabinet.
57. As a FOP, I want tables to be readable on a 375px screen with no horizontal page scroll, so I
    can use the app from my phone.

Reliability

58. As a FOP, I want a clear message when NBU is unavailable, with the ability to enter the rate
    manually, so my work isn't blocked.
59. As a FOP, I want amounts to always reconcile to the kopeck, so they match the statement and
    the Cabinet.
60. As the owner, I want automatic database backups, so I don't lose data on a server failure.

## Implementation Decisions

Architecture (ADR-001, ADR-006)

- Backend on ASP.NET Core 10 with Minimal APIs, EF Core and Npgsql. Frontend on Next.js, UI only;
  every `/api/*` request is proxied to the backend inside one Docker network, so from the browser
  it is a single origin.
- Deploy via Coolify: a Docker Compose resource with the `web` and `api` services. The database
  reuses the PostgreSQL instance already running on the owner's VPS — a dedicated role and
  database are created for this project, rather than a new Coolify PostgreSQL resource. Cron runs
  inside `api` as hosted services. The MVP needs no cron.
- The backend address is baked into the `web` image at build time, because Next.js rewrites are
  resolved at build time.

Tax engine (ADR-002)

- A separate class library with no packages and no access to the clock, database or network.
  Input: year configs, FOP settings, a list of income entries already in kopecks with a type and
  date, a list of payments, "today's" date. Output: income by period, accruals, obligations with
  statuses, balances per kind, limit status, warnings.
- `Obligation` is computed, not stored. Its key: year, quarter, kind, month for an advance.
  Statuses: `Upcoming`, `Due`, `Overdue`, `Done`.
- The quarter's EP and VZ are the rate on cumulative income minus what was already accrued for
  prior quarters, matching the declaration. ESV is computed over active months since the
  registration date, per the registration-month policy and the exemption flag.
- Balances per kind are independent. An overpayment carries forward within its own kind.
- The deadline calendar is parameterized: the ESV day, the declaration day count, the days to pay
  after the declaration, weekends, holidays, whether the payment deadline is counted from the
  statutory date, whether the payment deadline itself shifts off a weekend. Defaults reproduce
  the 2026 reference table.
- The limit is not prorated for a partial year. Thresholds at 85% and 100%, excess taxed at the
  excess rate.

Money and dates (ADR-003, ADR-004)

- Every amount is `long` in minor units: kopecks for UAH, cents for USD and EUR. Rate `int
  RateE4` = rate × 10⁴. Percentages as basis points. One rounding per operation, half away from
  zero. Implemented and covered by tests in `Money`.
- Operation date is `DateOnly` by Europe/Kyiv. Conversion from UTC happens in the API boundary
  adapter. The engine knows nothing about time zones. "Today" for statuses is computed by the API
  in Kyiv time and passed to the engine.

Data (domain-model)

- MVP entities: User, Settings, TaxYearConfig, BankAccount, Client, Transaction, BudgetPayment,
  FxRate, AuditLog. All except TaxYearConfig carry `UserId`; every query filters on it.
- Transaction stores `AmountMinor`, `Currency`, `RateE4`, `RateDate`, `RateSource`,
  `AmountUahKop`, `Kind`, `NonIncomeReason`. The hryvnia amount is fixed at write time.
- BudgetPayment stores kind, amount, year and quarter, or month for an advance.
- TaxYearConfig stores every rate and deadline rule, `Source`, `VerifiedAt`. Derived
  `EsvMonthlyKop` and `IncomeLimitKop` are recomputed on save. 2026 loads via seed data.
- Migrations run at `api` startup.
- AuditLog is written by a save interceptor for Transaction, BudgetPayment, Settings,
  TaxYearConfig, with before/after snapshots.

NBU rate

- An NBU client with a timeout. On an empty response (weekend), it falls back day by day, up to
  7 days; the actual rate date is stored. Cached in FxRate by currency and date. NBU
  unavailability surfaces to the client as an external-dependency error, not a 500; the form
  still allows a manual rate.

Authentication (ADR-005)

- ASP.NET Core Identity on schema v3, sign-in via Google, passkey via .NET 10 Identity's built-in
  support, cookie `HttpOnly; Secure; SameSite=Lax`, ForwardedHeaders behind Traefik. Email
  allowlist from configuration. Anti-forgery for mutating requests via a required
  `X-Requested-With` header.

API contract

- Resources: `auth`, `transactions`, `payments`, `settings`, `tax-years`, `fx`, `dashboard`,
  `periods/{year}`, `obligations/{year}`, `export`, `backup`, `restore`, `import/prototype`,
  `audit`, `health`.
- OpenAPI is generated by the backend. Frontend types are generated from it by a script, never
  hand-duplicated.
- Amounts in JSON as whole numbers of minor units. Dates as ISO `YYYY-MM-DD`.
- Restoring from a backup replaces the user's data inside a single database transaction.
  Prototype import is idempotent: imported records are tagged with a source external key.

Frontend

- shadcn/ui and Tailwind, color tokens from the prototype, dark theme following the system with a
  manual toggle. TanStack Query for data, TanStack Table for tables, TanStack Form for forms.
  Zustand is not used while there is no global client state.
- next-intl with two locales: Ukrainian by default, Russian second. All strings are externalized
  from the first screen; the language choice is stored in user settings and in a cookie.
- PWA: manifest, icons, a service worker for installability and shell caching. Offline data is
  not cached.

## Testing Decisions

What makes a good test here: it checks observable behavior through a public boundary and knows
nothing about internal structure. For the engine that means the input and output of pure
functions. For the API that means an HTTP request and response plus database state. A test never
mocks what it can stand up for real.

Seams

1. The engine's public API. Table-driven xUnit tests: the 2026 deadline reference table, the
   scenarios from the spec (registration mid-quarter, year rollover, refunds, monthly advances
   with an overpayment, currency receipts with rounding, the limit at its thresholds). This is
   the primary seam, and full coverage of the key scenarios is expected here. The Obsidian
   prototype is the source of expected numbers for reconciliation.
2. The HTTP API via `WebApplicationFactory` with a real Postgres in Testcontainers. Checked:
   allowlist and 401/403, transaction and payment validation, fixing the rate and hryvnia amount,
   the NBU rate falling back to a business day with a substituted HTTP handler, the change log,
   the backup/restore round trip, prototype-import idempotence, `dashboard` and `periods`
   responses matching the engine's computation on the same data.

No new seams are introduced. In the MVP, the frontend is verified by hand against each ticket's
acceptance criteria on phone and desktop. No direct component tests are written.

Existing examples: `MoneyTests` in the engine tests set the style for table-driven tests with
`InlineData`. There are no API tests in the repository yet; the API test project is created in
the database-schema ticket.

## Out of Scope

- CSV/XLSX statement import, monobank and PrivatBank APIs, request queueing, token encryption
  (Stage 2).
- Telegram and email reminders, .ics export (Stage 2).
- Clients with details, PDF invoices, F0103309 XML declarations, archiving (Stage 3).
- Multi-user mode, self-registration, offline data in the PWA.
- Automatic tax payment and declaration filing. The app deliberately does not do this.
- Automated UI tests.

## Further Notes

- Two rule interpretations are not yet confirmed by the owner and are made configurable: whether
  the EP/VZ payment deadline is counted from the declaration's statutory date with the payment
  deadline itself also shifted, and whether ESV in the registration month is the full amount.
  Defaults reproduce the 2026 reference table.
- The FOP is not registered yet. The registration date will be in the future; there are no real
  statements yet, so the engine is verified against synthetic data and the reference table.
- Advice for the owner at registration: file the Group 3 application together with the
  registration, otherwise the first partial month falls under the general tax system.
- NBU returns an empty array on weekends; this was verified by a live request. Holidays are
  treated as business days during martial law, so the holiday list in the config is empty.
- The owner implements the tickets by hand. This spec fixes the decisions so the tickets don't
  drift from each other.
