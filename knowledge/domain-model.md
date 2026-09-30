# Domain Model

Money: `long` in the minor unit of the currency (kopecks, cents). Rate: `int RateE4` = rate × 10⁴.
Percentages: basis points (`int`, 5% = 500). Operation dates: `DateOnly` by Europe/Kyiv.
Every entity except `TaxYearConfig` and `FxRate` has a `UserId`. Those two are shared by every owner.

## Entities

### User

Responsibilities: the data owner. An ASP.NET Core Identity user.

Fields: `Id`, `Email`, `DisplayName`, `CreatedAt`. Passkeys and external logins are stored by
Identity.

Relationships: 1-to-1 with `Settings`, 1-to-many with everything else.

---

### Settings

Responsibilities: the parameters of this specific FOP that affect the calculation.

Fields:

- `FopRegistrationDate: DateOnly?` FOP registration date. Until set, there are no obligations.
- `PaymentMode: Quarterly | MonthlyAdvance`.
- `EsvRegistrationMonthPolicy: FullMonth | Prorated`, defaults to `FullMonth`.
- `EsvExempt: bool` exemption from ESV for oneself (e.g. an employer pays it). Defaults to
  `false`.
- `TaxPaymentCountsFromStatutoryDeclarationDate: bool`, defaults to `true`.
- `ShiftTaxPaymentFromWeekend: bool`, defaults to `true`.
- `WeekendDays: DayOfWeek[]`, defaults to Saturday and Sunday.
- `Locale`, `Theme`, `DefaultCurrency`.

Relationships: belongs to `User`.

---

### TaxYearConfig

Responsibilities: all parameters of a tax year. The single source of truth for the engine. No
`UserId`.

Fields:

- `Year`.
- `MinWageKop` minimum wage as of January 1.
- `SingleTaxRateBp` (500), `MilitaryLevyRateBp` (100), `EsvRateBp` (2200), `ExcessRateBp` (1500).
- `EsvMonthlyKop` computed as `MinWageKop × EsvRateBp / 10000`, stored for transparency.
- `IncomeLimitMinWages` (1167), `IncomeLimitKop` computed and stored.
- `LimitWarnThresholdsPct` (85, 100).
- `EsvDeadlineDay` (19, inclusive, month after the quarter).
- `DeclarationDays` (40, calendar days after the end of the quarter).
- `TaxPaymentDaysAfterDeclaration` (10).
- `AdvanceRecommendedDay` (15, of the following month).
- `Holidays: DateOnly[]` non-working holidays. Empty during martial law.
- `Source` a reference to the legal source, `VerifiedAt: DateTimeOffset?`. An offset-aware
  timestamp and not a `DateOnly`, because it records when a person checked the numbers rather
  than a tax date. Writing any field of a year clears it, since a verification attests to the
  values that were compared against the legal source and not to the row.

Relationships: none. The engine receives the list of configs as input.

---

### BankAccount

Responsibilities: a FOP account or personal account, the source of an import.

Fields: `Bank: Monobank | PrivatBank | Other`, `Name`, `CurrencyCode` (ISO 4217 numeric, as the bank
reports it; only 980/840/978 map to a display currency), `Iban`, `AccountType` (the bank's own open
string, e.g. `fop`, `black`, `diia`), `IsFop` (`AccountType == "fop"`), `ExternalId` (the bank's
account id), `IsActive` (the owner is following this account for sync; always `false` for a
non-FOP account, and force-cleared for any account a later token save no longer reports),
`SyncedThrough?` (the sync cursor: the end of the last statement window whose rows are committed,
written in the same transaction as them; null until the first window lands, see Rule 12),
`HistoryImportedAt?` (when a window reaching the present was first committed; from then on the
history counts as imported whatever the cursor's age, and a restore clears it with the cursor),
`LastFailedAt?` and `LastFailure?` (the last failed sync other than a rejected token, one of
`BankUnreachable | BankTimeout | BankError | UnreadableAnswer | RateLimited | TokenUnreadable |
TooManyInOneSecond | Unexpected`; both set or both null, cleared when a window of the account imports). Unique per
(`UserId`, `Bank`, `ExternalId`).

Settings shows, per followed account, the month its cursor has reached (or that the history is
imported, once `HistoryImportedAt` is set), its latest `ImportBatch` as the last sync, and its last
failure.

A row exists for every account the token exposed, FOP or not, so settings can list an unsupported
type without a second call to the bank. Only a `fop` account can ever have `IsActive = true`.

The token itself does not live here: see `MonobankConnection` below (#75). `EncryptedToken` moved
off this entity because the connection — one token per owner — outlives any single account, and
disconnecting must not touch the `BankAccount` rows a sync already created.

A backup carries every account (never the connection or its token). A restore matches a file account
to the owner's row with the same `Bank` and `ExternalId` and keeps that row as it stands; an account
the owner does not hold is inserted. Reconnecting after a restore therefore finds the rows the restored
transactions point at, and the sync's `ExternalId` check sees them.

Relationships: belongs to `User`, has many `Transaction` and `ImportBatch`.

---

### MonobankConnection

Responsibilities: one owner's personal API token for one bank. One connection, and one encrypted
token, per owner — not per account (#75).

Fields: `UserId` (primary key), `EncryptedToken` (AES-256-GCM ciphertext, see ADR-011; never
returned by the API), `MonobankClientId` (the bank's own client id, kept only to help the owner
recognise which token is connected), `ConnectedAt`, `RejectedAt?` (set when monobank answered 401 or
403 to a statement call with this very token, compared by its ciphertext so a token saved meanwhile is
not marked; while set, no sync of the owner runs and "sync now" answers 409), `WebhookSecret` (64
random hex characters, unique, the path segment of the owner's webhook URL; drawn again on every token
save, ADR-012), `WebhookUrl?` (the URL monobank last accepted for this token, or null),
`WebhookFailedAt?` and `WebhookFailure?` (the last failed registration, cleared by a successful one).

Settings shows the webhook as `Off` (no public base URL configured), `Pending`, `Registered` (the
stored URL is the wanted one) or `Failed` (with the time and reason).

The token is validated against the bank's `client-info` endpoint at the moment it is saved; an
invalid token is rejected and nothing is stored. Saving a valid token reconciles `BankAccount` rows
against the accounts the bank now reports: an existing row not among them has `IsActive` cleared
and stops being followed, a new `fop` row gets `IsActive = true`, an existing `fop` row keeps the
owner's choice (an account that reappears stays unfollowed until the owner follows it again), and
every other type stays unselected and not selectable. Saving a token clears `RejectedAt`; when it was
set, the owner's followed accounts are queued and resume from their cursors. Disconnecting deletes this row only;
`BankAccount` rows, and anything imported against them, stay. It also asks the bank to remove the
webhook, so the old secret path answers 404 from then on.

Relationships: belongs to `User`, 1-to-1.

---

### Client

Responsibilities: a counterparty for linking receipts and invoices.

Fields: `Name`, `Country`, `Address`, `Email`, `VatId`, `DefaultCurrency`, `Notes`. Only `Name` is
used in the MVP. A client is created the first time a receipt names it and is unique by name per owner.

Relationships: belongs to `User`, has many `Transaction` and `Invoice`.

---

### Transaction

Responsibilities: a single account movement with a type and a hryvnia equivalent.

Fields:

- `ValueDate: DateOnly` the credit date by Kyiv time. Determines the period.
- `BankTime: DateTimeOffset?` the original bank timestamp.
- `AmountMinor: long`, `Currency` (UAH, USD, EUR).
- `RateE4: int` (10000 for UAH), `RateDate: DateOnly?` the actual NBU rate date, `RateSource: Nbu
  | Manual`. Both are null for UAH; `RateDate` is null for a manual rate.
- `AmountUahKop: long` = roundHalfUp(`AmountMinor` × `RateE4` / 10⁴). Fixed at write time.
- `Kind: Income | RefundToClient | OwnTransfer | FxSale | OwnDeposit | ErroneousReturn |
  OtherNonIncome`.
- `NonIncomeReason?` text for a non-income entry.
- `RefundsTransactionId?` the receipt a `RefundToClient` reverses: a nullable self-reference, set
  only on `RefundToClient` and only to the same owner's `Income` row in the refund's currency. The
  refunds linked to one receipt total at most its `AmountMinor`, compared in that currency, not in
  hryvnia. A receipt with linked refunds cannot be deleted and keeps its kind (`Income`) and
  currency.
- `ClientId?`, `InvoiceId?`, `InvoiceNumber?`, `Description`, `Counterparty`.
- `BankAccountId?` the account an imported row came from, and `ExternalId?` the bank's operation id,
  unique together. Both are set on an imported row and neither on a typed one.
- `BankTime?` the bank's own instant for the operation; `ValueDate` is its Kyiv date.
- `Counterparty?` the counterparty name as the bank sent it; the client is linked by the same name.
- `ImportBatchId?` the sync run that inserted the row.
- `ReviewStatus: Confirmed | NeedsReview | Dismissed`. Every imported row starts `NeedsReview` with a
  suggested kind; typed rows, and every row that existed before #76, are `Confirmed`. Confirming or
  saving any edit sets `Confirmed`, so a `NeedsReview` row carries no owner decision. Between
  `Confirmed` and `NeedsReview` it never changes a figure. `Dismissed` is a deleted imported row kept
  as a tombstone: it counts nowhere and is never shown, and it holds its `ExternalId` so a sync does
  not record the operation again (Rule 12).
- `CreatedAt`, `UpdatedAt`.

Rule: period income includes `Income` with a plus sign and `RefundToClient` with a minus sign. A
linked refund is excluded with its receipt (Rule 8).

Relationships: belongs to `User`, optionally `BankAccount`, `Client`, `Invoice`, `ImportBatch`.

---

### BudgetPayment

Responsibilities: an actual payment into the budget.

Fields: `PaidOn: DateOnly`, `Kind: SingleTax | MilitaryLevy | Esv`, `AmountKop` (positive),
`PeriodYear`, `PeriodQuarter?` (1–4), `PeriodMonth?` (1–12, for advances), `Note?`, `BankAccountId?`
and `ExternalId?` (the bank operation it was confirmed or linked from, set together or not at all
and unique together, #80), `CreatedAt`, `UpdatedAt`.

Rule: exactly one of `PeriodQuarter` and `PeriodMonth` is set, held by validation and by a database
check constraint. The period is the one the owner names and is kept and shown, never taken from
`PaidOn`: a Q4 payment made the next February still names Q4. Which obligation a payment settles is
Rule 7's allocation, oldest debt of its kind first, whatever quarter or month it names.

Relationships: belongs to `User`; optionally to the `BankAccount` of its operation.

---

### BudgetPaymentCandidate

Responsibilities: a settled debit from a followed UAH FOP account to a Treasury account, waiting for
the owner to confirm what it paid (#80, Rule 12). Written by the monobank sync, resolved by the owner
on the review screen. Not audited: the payment a confirmation creates or links is.

Fields: `BankAccountId`, `ExternalId` (the bank's operation id, unique together with
`BankAccountId` whatever the status; a sync only inserts), `BankTime`, `AmountKop` (what left the
account, positive), `CounterIban` (capitals, no spaces), `CounterName?`, `Purpose?` (the bank's
description and the payer's comment), `Status: Pending | Confirmed | Dismissed`, `ConfirmedKind?`
(set exactly when `Confirmed`; the kind the next candidate to the same IBAN is suggested),
`CreatedAt`, `ResolvedAt?`. The suggested kind and period are not stored: they are read off the
learned kinds, the purpose and the current ledger each time the list is shown.

Relationships: belongs to `User` and `BankAccount`.

---

### Obligation (computed)

Responsibilities: what is due and when. Not stored, computed by the engine.

Fields: `Year`, `Quarter`, `Kind: SingleTax | MilitaryLevy | Esv | Declaration | MonthlyAdvance`,
`Month?`, `AccruedKop`, `StatutoryDate`, `DueDate` (shifted), `PaidKop` (what the kind's
payments settled, oldest debt first, Rule 7), `RemainingKop` (never negative; a kind's overpayment
is credit on its ledger, not on one obligation), `Status: Upcoming | Due | Overdue | Done`, `CumulativeIncomeKop` for
the declaration.

---

### MonthlyAdvance (computed)

Responsibilities: Rule 6's recommendation for one month in `MonthlyAdvance` mode. Not stored and
not an obligation; read off the Rule 7 allocation.

Fields: `Year`, `Month`, `IncomeKop`, per kind (`SingleTax`, `MilitaryLevy`, `Esv`) the month's
`AccruedKop` and the `RemainingKop` its quarter's allocation left on it, `RecommendedKop` (the three
remainders added, a figure to read like the tax burden), `RecommendedDate`.

---

### FxRate

Responsibilities: a cache of NBU exchange rates.

Fields: `Currency`, `Date` (the requested date), `RateE4`, `RateDate` (the NBU date the rate belongs
to, earlier than `Date` after a fallback), `FetchedAt`. Key (`Currency`, `Date`). A future `Date` is
never cached.

---

### Backup file

Responsibilities: one owner's data as a JSON file to download and restore. Not stored.

Fields: `SchemaVersion` (3), `Settings?`, `Clients`, `Transactions`, `BudgetPayments`,
`BankAccounts`, `ImportBatches`, `BudgetPaymentCandidates`, each row with its id and every stored column except `UserId` and a
bank account's sync state (`SyncedThrough`, `HistoryImportedAt`, `LastFailedAt`, `LastFailure`),
which a restore clears.
`TaxYearConfig` and `FxRate` are left out because they are shared. The monobank connection is never
in it, so the file carries no bank access. Version 2 added the bank accounts, the import batches and
the transactions' import fields (#76); version 3 added the budget payment candidates, all statuses,
and the payments' bank operation (#80). A version 1 file still restores, read as having none of them
and every transaction `Confirmed`, and a version 2 file as having no candidates and every payment
typed by the owner; a file of a version this build does not know is refused by its
version number rather than by whichever field it added.

A restore replaces the owner's settings, clients, transactions, payments, candidates and import batches in one
database transaction and passes every row through the endpoints' own validation, refund links
included; any violation changes nothing. Bank accounts are matched rather than replaced (see
`BankAccount`). Ids are kept, so a restore after a wipe reproduces the same file. When another owner
still holds one of the ids, every id in the file is replaced by a fresh one and the links follow.

---

### Prototype file

Responsibilities: the owner's export from the prototype this app replaces, merged in once. Not
stored. No sample of the export exists in the repository, so this shape is derived from the spec and
the ticket and read strictly: any other shape is refused with an error naming the field.

Fields: an object with `incomes` and/or `mpaid`; `settings` and `done` are accepted and ignored; any
other key is refused.

- `incomes`: an array (at most 10,000) of `{ date, amount, currency, rate?, uah, client?, invoice?,
  comment? }`. `date` is `YYYY-MM-DD`; `currency` is `UAH`, `USD` or `EUR`; `amount`, `rate` and
  `uah` are JSON numbers or numeric strings, read from their text, never through a double. `amount`
  has at most 2 decimals; `rate` (required for USD/EUR, absent or 1 for UAH) and `uah` are rounded
  half away from zero to 4 and 2 decimals. `uah` must equal `amount × rate` by Rule 2's formula.
- `mpaid`: an object of `YYYY-MM` keys to `true` or `false`, the prototype's "paid" checkmark per
  month. A month marked `true` cannot be after the current one; a `false` month writes nothing and
  is not checked.

Mapping: an income becomes an `Income` transaction with `AmountUahKop` = `uah` in kopecks and a
`Manual` rate (a UAH income has no rate source, as every UAH row). It passes the transaction
endpoint's own validation, so a future date, a NUL or an over-long text is refused. A month marked
`true` becomes one month-period payment per kind (EP, VZ, ESV) for what that month accrued (Rule 6's
year-to-date split), dated on the month's recommended advance date, or today if that is later. A kind
that accrued nothing is skipped. The amount is the app's own accrual for the month, not what the
owner actually paid: the prototype stores only a checkmark. If the owner paid a different sum, they
correct it on Payments. A kind that already has a payment naming that month is skipped, whatever its
amount. This needs `Settings.FopRegistrationDate` and the year's
`TaxYearConfig`; without them the import is refused.

Idempotence: an import never edits or deletes. An income is already present when the owner has as
many `Income` rows with the same date, currency, amount, hryvnia amount and client as the file
holds; a month's kind is already present when any payment names that kind and month. Re-importing a
file adds nothing; a file with one more record adds exactly that one. A prototype record changed
between imports (another amount or date) no longer matches, so it is added beside the old one, which
the owner deletes by hand. A text-only change (invoice, comment) is not a new record. The prototype's
`settings` are not applied: their names are unknown, the tax parameters in them belong to the shared
`TaxYearConfig`, and a wrong guess would move every accrual. `done` holds the prototype's quarterly
checkmarks, which this app derives from payments instead.

`POST /api/import/prototype?dryRun=true` runs the same import and rolls it back, so the counts the
owner confirms are the counts the import writes.

---

### AuditLog

Responsibilities: change log for transactions, budget payments, settings and year parameters. One
row (`AuditEntry`, table `AuditLog`) per created, changed or deleted record. Append-only.

Fields: `Entity: Transaction | BudgetPayment | Settings | TaxYearConfig | Backup`, `EntityId` (the
record's key: a GUID, the year, or the owner's id for Settings; empty for Backup),
`Action: Create | Update | Delete | Restore`,
`Before: jsonb` (null on Create), `After: jsonb` (null on Delete), `At` (UTC instant, shown in
Kyiv time), `UserId`.

A snapshot holds the record's fields by their API names; money stays integer kopecks. It leaves out
the key, `UserId`, `CreatedAt` and `UpdatedAt`, and a transaction's snapshot carries `clientName`
instead of `clientId`. A save that changes nothing but `UpdatedAt` writes no entry.

`UserId` is the record's owner. `TaxYearConfig` is shared by every allowlisted user, so its entry
belongs to the user who changed it. Nobody reads another user's entries.

A restore from backup writes one `Backup`/`Restore` entry with the restored counts instead of one
entry per inserted row. The log is not part of a backup and a restore never replaces it.

A prototype import writes the usual `Create` entry per inserted row: it adds to the owner's data
rather than replacing it, so each imported record keeps its own history from its first edit on.

---

### NotificationChannel, Reminder (Stage 2)

`NotificationChannel`: `Kind: Telegram | Email`, `Address` (chat id or email), `Enabled`.
`Reminder`: `ObligationKey`, `OffsetDays` (7, 1, 0), `ScheduledAt`, `SentAt?`, `ChannelId`,
`Status`.

### ImportBatch

Responsibilities: one sync run for one account (#76). Written by the monobank sync worker, never by
the owner, and not audited: the rows it imported each get their own `Create` entry.

Fields: `Source: Monobank` (CSV and PrivatBank join when they are built), `BankAccountId`, `From` and
`To` (the statement window as instants, since a bank window is not a whole number of Kyiv days),
`ImportedCount` (rows written), `SkippedCount` (credits not written, see Rule 12), `CreatedAt`.
Settings shows each account's latest batch as its last sync.

### ForeignDebit

Responsibilities: the foreign leg of a possible currency sale (#78). A settled debit on one of the
owner's foreign-currency FOP accounts, written by the monobank sync and read only by the sale
pairing, so a UAH credit read in any later or earlier window still finds it (Rule 12). It is never a
`Transaction`, never shown, not audited and not in the backup: a restore clears the cursors and the
walk reads it again.

Fields: `BankAccountId`, `ExternalId` (the bank's operation id, unique together with
`BankAccountId`; a sync only inserts), `BankTime`, `AmountMinor` (what left the account, positive),
`Currency`.

### Invoice (Stage 3)

`Number`, `ClientId`, `IssueDate`, `DueDate`, `Currency`, `AmountMinor`, `Items: jsonb`,
`Status: Draft | Sent | Paid`, `PdfPath`.
