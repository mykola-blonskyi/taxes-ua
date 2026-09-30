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

### InvoicingDetails, InvoicingPaymentDetails

Responsibilities: the owner's own requisites for the invoices of #89. One `InvoicingDetails` row per
owner (an owner who never saved one reads the defaults and stores nothing) and at most one
`InvoicingPaymentDetails` row per owner and currency.

`InvoicingDetails` fields:

- `SellerNameUk`, `SellerNameEn`, `AddressUk`, `AddressEn`: free text, empty until entered.
- `Rnokpp`: empty or exactly 10 digits.
- `AcceptanceClauseEn/Uk`, `FeesClauseEn/Uk`, `TaxStatusClauseEn/Uk`: the bilingual clause texts.
  They start as the defaults (payment of the invoice is acceptance of the services, deemed rendered in
  full and without claims; bank fees are borne by the payer; the seller is a single tax payer not
  registered for VAT) and can be edited but not emptied. The defaults are a starting point, not legal
  advice.
- `SignatureImage: bytes?`, `SignatureContentType: image/png | image/jpeg`, `SignatureUpdatedAt`: an
  optional signature, at most 512 KB, stored in the database and checked against its type's magic bytes.
  It is served only to its owner by `GET /api/settings/invoicing/signature` with
  `Cache-Control: private`, and is replaced or removed without touching the other fields.

`InvoicingPaymentDetails` fields: `Currency: UAH | USD | EUR`, `Iban` (a Ukrainian IBAN: `UA` and 27
letters or digits, valid modulo 97; stored upper-case without spaces), `BeneficiaryBank`, `Swift` (8 or
11 letters and digits, upper-case), and the optional free-text `IntermediaryBank`, `IntermediarySwift`
and `IntermediaryAccount` the owner copies from the bank app. A currency with nothing entered has no
row. Whether the details are complete enough to issue an invoice is for the invoice to check.

Prefill from monobank is a read that stores nothing. For the FOP accounts in UAH,
USD or EUR (a followed one first, one per currency) it suggests the stored `BankAccount` IBAN with the constants `JSC Universal Bank, Kyiv` and
`UNJSUAUKXXX` (all monobank accounts are held at Universal Bank), and the Ukrainian name from a fresh
`client-info` call through the rate gate. The RNOKPP, the addresses, the Latin name and the
intermediary banks are not in the personal API and stay owner-entered. A suggestion is applied to the
form only when the owner accepts it and reaches the database only when they save: a later change at
the bank never alters saved details.

Audited as `InvoicingDetails` (both tables). A snapshot carries the image's size in bytes
(`signatureImageBytes`) instead of the image.

Relationships: belong to `User`.

---

### DeclarationDetails

Responsibilities: what the declaration's header needs beyond the invoicing requisites (Rule 15). One
row per owner, key `UserId`; an owner who never saved one reads an empty set and stores nothing.

Fields: `TaxOfficeRegion: int?` (C_REG, 1 to 99) and `TaxOfficeDistrict: int?` (C_RAJ, 0 to 99), both
set or both empty; `KvedCodes: string[]` (each `NN.NN`, distinct, at most 20, in the owner's order, the
first the main activity); `Address` (as in the register, at most 500 characters). The name and RNOKPP
are not stored here: they are `InvoicingDetails.SellerNameUk` and `Rnokpp`, and `GET
/api/settings/declaration` echoes them read-only. An incomplete set saves; completeness is a readiness
item (`MissingDetails: Name | Rnokpp | TaxOffice | Kved | Address`).

Audited as `DeclarationDetails`. Relationships: belongs to `User`.

---

### DeclarationFiling

Responsibilities: the owner's mark that a quarter's declaration was filed in the Cabinet (Rule 15).
Key (`UserId`, `Year`, `Quarter`).

Fields: `FiledOn: DateOnly` (after the quarter's end, not after today in Kyiv), `Type: Reporting |
NewReporting | Clarifying` (C_DOC_STAN 1, 2, 3), `FiledIncomeKop` (line 08 when marked; a different
line 08 now means changed since filing), `CreatedAt`, `UpdatedAt`. Marking again replaces the row,
undoing deletes it. Audited as `DeclarationFiling`. Relationships: belongs to `User`.

---

### DeclarationFigures (computed)

Responsibilities: the group 3 lines of one quarter's declaration (Rule 15). Not stored; read off the
year's accruals.

Fields: `Year`, `Quarter`, `IncomeKop` (06, 08), `SingleTaxKop` (11, 12), `PreviousSingleTaxKop` (13),
`SingleTaxPayableKop` (14.1, 14), `MilitaryLevyKop` (23), `PreviousMilitaryLevyKop` (24),
`MilitaryLevyPayableKop` (25), `EsvKop?` (21, Q4 only). The payable lines are negative after a refund
that shrank the cumulative income.

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

Responsibilities: a counterparty for linking receipts and invoices, and the buyer's details an invoice
carries (#90).

Fields: `Name` (1 to 200 characters), `Address` (the legal address, up to 500), `Country` (ISO 3166-1
alpha-2, stored in upper case), `VatId` (the tax or VAT id, up to 50), `Email` (up to 254),
`DefaultCurrency?` (`UAH`, `USD` or `EUR`), `Notes` (up to 2000). Only `Name` is required; a blank
optional field is stored as null. The API also returns `ReceiptCount`, the receipts linked to the client.

A client is created either by the owner on the Clients tab of Settings or, name only, the first time
a receipt names it (also from a bank import); the receipt's client name then finds it again. Names are
unique per owner, compared exactly after trimming (case and inner spaces count), and the owner can
complete a name-only client in place. Renaming changes `Name` only, so every receipt stays linked; a
later bank import whose counterparty carries the old name creates a client under that name again. A
client with linked receipts or any invoice cannot be deleted (409); one without can. A
dismissed import is not a receipt and does not block the deletion.

Audited like the other records: `Create`, `Update` and `Delete` entries, so creating a receipt with a
new client name also logs the client's `Create`.

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

### TaxReserve (computed)

Responsibilities: how much to keep aside for taxes (Rule 13). Not stored; read off the accruals and
the Rule 7 allocation, and never fed back into a balance.

Fields: per receipt `SetAside` (`SingleTaxKop`, `MilitaryLevyKop`; zero for a non-income kind,
negative for a refund, absent for a row Rule 8 excludes); in total `Dues`, one per due date, oldest
first, each with the `SingleTaxKop`, `MilitaryLevyKop` and `EsvKop` still needed by then, and
`TotalKop` (the dues added).

---

### FxRate

Responsibilities: a cache of NBU exchange rates.

Fields: `Currency`, `Date` (the requested date), `RateE4`, `RateDate` (the NBU date the rate belongs
to, earlier than `Date` after a fallback), `FetchedAt`. Key (`Currency`, `Date`). A future `Date` is
never cached.

---

### Backup file

Responsibilities: one owner's data as a JSON file to download and restore. Not stored.

Fields: `SchemaVersion` (7), `Settings?`, `Clients`, `Transactions`, `BudgetPayments`,
`BankAccounts`, `ImportBatches`, `BudgetPaymentCandidates`, `InvoicingDetails?` (with its per-currency
payment details and the signature as base64 with its content type), `Invoices` (with their lines, their
number as year and sequence, the frozen snapshot and the frozen signature as base64), `DeclarationDetails?`,
`DeclarationFilings`, each row with its id and every stored column except `UserId`, an invoice's
`TotalMinor` (recomputed from its lines) and a
bank account's sync state (`SyncedThrough`, `HistoryImportedAt`, `LastFailedAt`, `LastFailure`),
which a restore clears.
`TaxYearConfig` and `FxRate` are left out because they are shared. The monobank connection is never
in it, so the file carries no bank access. Version 2 added the bank accounts, the import batches and
the transactions' import fields (#76); version 3 added the budget payment candidates, all statuses,
and the payments' bank operation (#80); version 4 added the invoicing details (#91); version 5 added the
clients' details (#90); version 6 added the invoices (#92); version 7 added the declaration details and
the filed marks (#110). A version 1 file still restores, read as having none of them and every transaction
`Confirmed`, a version 2 file as having no candidates and every payment typed by the owner, a version 1 to 3
file as having no invoicing details, so the owner's are cleared like the rest, a version 1 to 4 file as
having no details on any client, a version 1 to 5 file as having no invoices, and a version 1 to 6 file as
having no declaration details and nothing marked filed; a file of a version this build does not know is
refused by its version number rather than by whichever field it added.

A restore replaces the owner's settings, invoicing details, declaration details, filed marks, clients, invoices, transactions, payments, candidates and import batches in one
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

Responsibilities: change log for transactions, clients, invoices, budget payments, settings and year parameters. One
row (`AuditEntry`, table `AuditLog`) per created, changed or deleted record. Append-only.

Fields: `Entity: Transaction | BudgetPayment | Settings | InvoicingDetails | TaxYearConfig | Backup | Client |
Invoice | DeclarationDetails | DeclarationFiling`, `EntityId` (the record's key: a GUID, the year, the owner's
id for Settings, InvoicingDetails and DeclarationDetails, `ownerId/year/quarter` for DeclarationFiling; empty
for Backup),
`Action: Create | Update | Delete | Restore`,
`Before: jsonb` (null on Create), `After: jsonb` (null on Delete), `At` (UTC instant, shown in
Kyiv time), `UserId`.

A snapshot holds the record's fields by their API names; money stays integer kopecks. It leaves out
the key, `UserId`, `CreatedAt` and `UpdatedAt`, and a transaction's snapshot carries `clientName`
instead of `clientId`, and an invoicing or invoice snapshot carries `signatureImageBytes` instead of the image.
A save that changes nothing but `UpdatedAt` writes no entry.

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

### Invoice

Responsibilities: a bilingual English/Ukrainian invoice to one client (#92), the primary document for
a service export (Rule 14). The PDF is rendered on request, never stored.

Fields:

- `ClientId` (the owner's client; a client with invoices cannot be deleted), `Status: Draft | Issued |
  Cancelled`, `IssueDate`, `DueDate` (not before the issue date), `Currency: UAH | USD | EUR`.
- `NumberYear`, `NumberSequence`: null on a draft; set at issue to the issue date's year and the next
  sequence of that year. The number reads `YYYY-NNN` (`2026-001`). Unique per owner, year and sequence.
- `Lines: jsonb`, at most 50, each `DescriptionEn`, `DescriptionUk` (up to 500 characters each),
  `Unit: Service | Hour | Day | Month` (printed as the fixed bilingual pairs "service / послуга", "hour /
  година", "day / день", "month / місяць"), `QuantityThousandths` (a quantity above 0 and up to
  1 000 000 with at most three decimals, as whole thousandths) and `RateMinor` (0 or more, in the
  currency's minor units).
- `TotalMinor`: the sum of the line amounts, written with the lines. A line's amount is
  `QuantityThousandths × RateMinor / 1000`, rounded once to a whole minor unit, half away from zero
  (Rule 10). The printed lines always add up to the printed total.
- `Snapshot: jsonb?`, `SignatureImage?`, `SignatureContentType?`: null on a draft; at issue, a copy of the
  seller (both names, RNOKPP, both addresses), the buyer (name, address, country code and its English
  name, tax id, email), the payment details of the invoice's currency and the six clause texts, and the
  signature image as it stood then (ADR-013).
- `CancelReason?` (up to 500), `IssuedAt?`, `CancelledAt?`, `CreatedAt`, `UpdatedAt`.

A draft is created, edited, duplicated and deleted freely, and its PDF preview reads the owner's
invoicing details and the client live, marked DRAFT. Issuing an issued invoice returns it unchanged;
issuing a cancelled one is refused. An issued invoice is never edited or deleted (409): it is cancelled
with a reason and keeps its number, and its PDF, marked CANCELLED, is still rendered from the
snapshot. Duplicating any invoice creates a draft dated today with the source's client, currency, lines
and payment term. Paid and overdue come with the receipt links of #93.

Audited as `Invoice`: `Create`, `Update` (edits, issue, cancel) and `Delete` of a draft.

Relationships: belongs to `User` and `Client`.
