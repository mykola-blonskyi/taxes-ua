# Domain Model

Money: `long` in the minor unit of the currency (kopecks, cents). Rate: `int RateE4` = rate × 10⁴.
Percentages: basis points (`int`, 5% = 500). Operation dates: `DateOnly` by Europe/Kyiv.
Every entity except `TaxYearConfig` has a `UserId`.

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
used in the MVP.

Relationships: belongs to `User`, has many `Transaction` and `Invoice`.

---

### Transaction

Responsibilities: a single account movement with a type and a hryvnia equivalent.

Fields:

- `ValueDate: DateOnly` the credit date by Kyiv time. Determines the period.
- `BankTime: DateTimeOffset?` the original bank timestamp.
- `AmountMinor: long`, `Currency` (UAH, USD, EUR).
- `RateE4: int` (10000 for UAH), `RateDate: DateOnly` the actual NBU rate date, `RateSource: Nbu
  | Manual`.
- `AmountUahKop: long` = roundHalfUp(`AmountMinor` × `RateE4` / 10⁴). Fixed at write time.
- `Kind: Income | RefundToClient | OwnTransfer | FxSale | OwnDeposit | ErroneousReturn |
  OtherNonIncome`.
- `NonIncomeReason?` text for a non-income entry.
- `ClientId?`, `InvoiceId?`, `InvoiceNumber?`, `Description`, `Counterparty`.
- `ExternalId?` the bank's transaction ID, unique together with `BankAccountId`.
- `ImportBatchId?`, `ReviewStatus: Confirmed | NeedsReview`.
- `CreatedAt`, `UpdatedAt`.

Rule: period income includes `Income` with a plus sign and `RefundToClient` with a minus sign.

Relationships: belongs to `User`, optionally `BankAccount`, `Client`, `Invoice`, `ImportBatch`.

---

### BudgetPayment

Responsibilities: an actual payment into the budget.

Fields: `PaidOn: DateOnly`, `Kind: SingleTax | MilitaryLevy | Esv`, `AmountKop`, `PeriodYear`,
`PeriodQuarter?` (1–4), `PeriodMonth?` (1–12, for advances), `Note`, `CreatedAt`.

Relationships: belongs to `User`.

---

### Obligation (computed)

Responsibilities: what is due and when. Not stored, computed by the engine.

Fields: `Year`, `Quarter`, `Kind: SingleTax | MilitaryLevy | Esv | Declaration | MonthlyAdvance`,
`Month?`, `AccruedKop`, `StatutoryDate`, `DueDate` (shifted), `PaidKop`, `BalanceKop` (positive =
owed, negative = overpaid), `Status: Upcoming | Due | Overdue | Done`, `CumulativeIncomeKop` for
the declaration.

---

### FxRate

Responsibilities: a cache of NBU exchange rates.

Fields: `Currency`, `Date`, `RateE4`, `FetchedAt`. Key (`Currency`, `Date`).

---

### AuditLog

Responsibilities: change log for transactions, payments and settings.

Fields: `Entity`, `EntityId`, `Action: Create | Update | Delete`, `Before: jsonb`, `After: jsonb`,
`At`, `UserId`.

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
