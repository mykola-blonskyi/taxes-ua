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

Fields: `Bank: Monobank | PrivatBank | Other`, `Name`, `Currency`, `Iban`, `IsFop`, `ExternalId`,
`EncryptedToken?`, `IsActive`.

Relationships: belongs to `User`, has many `Transaction`.

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
- `ExternalId?` the bank's transaction ID, unique together with `BankAccountId`.
- `ImportBatchId?`, `ReviewStatus: Confirmed | NeedsReview`.
- `CreatedAt`, `UpdatedAt`.

Rule: period income includes `Income` with a plus sign and `RefundToClient` with a minus sign. A
linked refund is excluded with its receipt (Rule 8).

Relationships: belongs to `User`, optionally `BankAccount`, `Client`, `Invoice`, `ImportBatch`.

---

### BudgetPayment

Responsibilities: an actual payment into the budget.

Fields: `PaidOn: DateOnly`, `Kind: SingleTax | MilitaryLevy | Esv`, `AmountKop` (positive),
`PeriodYear`, `PeriodQuarter?` (1–4), `PeriodMonth?` (1–12, for advances), `Note?`, `CreatedAt`,
`UpdatedAt`.

Rule: exactly one of `PeriodQuarter` and `PeriodMonth` is set, held by validation and by a database
check constraint. A payment belongs to the year and quarter of its period, never of `PaidOn`: a Q4
payment made the next February settles Q4. A monthly payment credits the quarter containing the
month.

Relationships: belongs to `User`.

---

### Obligation (computed)

Responsibilities: what is due and when. Not stored, computed by the engine.

Fields: `Year`, `Quarter`, `Kind: SingleTax | MilitaryLevy | Esv | Declaration | MonthlyAdvance`,
`Month?`, `AccruedKop`, `StatutoryDate`, `DueDate` (shifted), `PaidKop` (what the kind's
payments settled, oldest debt first, Rule 7), `RemainingKop` (never negative; a kind's overpayment
is credit on its ledger, not on one obligation), `Status: Upcoming | Due | Overdue | Done`, `CumulativeIncomeKop` for
the declaration.

---

### FxRate

Responsibilities: a cache of NBU exchange rates.

Fields: `Currency`, `Date` (the requested date), `RateE4`, `RateDate` (the NBU date the rate belongs
to, earlier than `Date` after a fallback), `FetchedAt`. Key (`Currency`, `Date`). A future `Date` is
never cached.

---

### Backup file

Responsibilities: one owner's data as a JSON file to download and restore. Not stored.

Fields: `SchemaVersion` (1), `Settings?`, `Clients`, `Transactions`, `BudgetPayments`, each row with
its id and every stored column except `UserId`. `TaxYearConfig` and `FxRate` are left out because
they are shared. A restore replaces the owner's four tables in one database transaction and passes
every row through the endpoints' own validation, refund links included; any violation changes
nothing. Ids are kept, so a restore after a wipe reproduces the same file. When another owner still
holds one of the ids, every id in the file is replaced by a fresh one and the links follow.

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

---

### NotificationChannel, Reminder (Stage 2)

`NotificationChannel`: `Kind: Telegram | Email`, `Address` (chat id or email), `Enabled`.
`Reminder`: `ObligationKey`, `OffsetDays` (7, 1, 0), `ScheduledAt`, `SentAt?`, `ChannelId`,
`Status`.

### ImportBatch (Stage 2)

`Source: Csv | Monobank | PrivatBank`, `BankAccountId`, `FromDate`, `ToDate`, `FileName?`,
`ImportedCount`, `SkippedCount`, `CreatedAt`.

### Invoice (Stage 3)

`Number`, `ClientId`, `IssueDate`, `DueDate`, `Currency`, `AmountMinor`, `Items: jsonb`,
`Status: Draft | Sent | Paid`, `PdfPath`.
