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
- `BackOnGroup3From: YearQuarter?` (stored as `BackOnGroup3FromYear` and `BackOnGroup3FromQuarter`,
  both set or both null) the quarter the FOP is back on group 3 from after a limit crossing (Rule 4).
  Null by default, and then nothing after a crossing is computed. Audited with the rest of the row and
  carried in the backup from schema version 10.
- `Group3Since: DateOnly?` the day the DPS register has group 3 from (Rule 8): the registration date,
  or the first day of a later quarter. Null means the registration date, which is stored as null; a
  settings save clears a start that is not a quarter start after the new registration date. The
  engine's `Group3Start` is the later of the two.
- `Group3ConfirmedOn: DateOnly?` and `Group3ReceiptNumber: string?` the DPS receipt for the group 3
  application, both set or both null. Group 3 is unconfirmed while they are null, and every figure is
  provisional (Rule 8).
- `DpsFopRegistered`, `DpsEsvRegistered`, `DpsAccountsRegistered: bool` the owner's manual ticks on
  the "Status with the DPS" checklist. Nothing reads them but the checklist.

The group 3 fields and the ticks are written through `PUT /api/settings/dps-status`, not the settings
request, so the FOP form cannot wipe them. Audited with the rest of the row and carried in the backup
from schema version 16.

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
`UNJSUAUKXXX` (all monobank accounts are held at Universal Bank), and the Ukrainian name from
`client-info`, read through `MonobankClientInfoReader` like the jars. Within a minute of the token save
or of any jar read the name comes from that call's answer and the bank is not asked again. The reader
takes the rate gate's slot only when it is free, so the prefill answers `429` with `Retry-After` (and
the screen says when to try again) only when the slot was spent without an answer to keep, and a token
monobank rejects answers `409` so the owner connects again. The RNOKPP, the addresses, the Latin name and the
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
set or both empty; `TaxOfficeName` (the office's name for the form's HSTI, as the Cabinet shows it next
to the code, at most 200 characters, empty when not set); `KvedCodes: string[]` (each `NN.NN` and a class of КВЕД ДК 009:2010, refused as `kved_unknown` otherwise; distinct, at most 20, in the owner's order, the
first the main activity); `Address` (as in the register, at most 500 characters); `FullName` (surname,
given name and patronymic as in the registration documents, at most 200 characters, HNAME; empty means
the invoicing name); `Phone` (HTEL, stored as `+380` and nine digits, typed with spaces, brackets or dashes,
or from `0XXXXXXXXX`; empty leaves it out); `ReportEmail` (HEMAIL, empty leaves it out; its own setting,
which stays empty until saved; the form offers the confirmed email channel's address as a one-click suggestion). The RNOKPP and the invoicing name
are not stored here: they are `InvoicingDetails.SellerNameUk` and `Rnokpp`, and `GET
/api/settings/declaration` echoes them read-only. The suggestion comes from the channels the web loads
from `GET /api/notifications/channels`, not from this endpoint. An incomplete set saves; completeness is a readiness
item (`MissingDetails: Name | Rnokpp | TaxOffice | Kved | Address`; `TaxOffice` covers the codes and
the name). A stored code the classifier does not know, which only a restored backup can hold, is its own
readiness item (`UnknownKvedCodes`, also on `GET /api/settings/declaration`) and blocks the declaration; the
backup restore accepts it, the settings form refuses to save it. `GET /api/settings/declaration/kved-classes`
serves the classifier, code and Держстат's Ukrainian name, for the form to show the name under each code.

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

### DeclarationFile

Responsibilities: the F0103309 XML the owner last prepared for a quarter and a declaration type, and
on the year's last group 3 declaration its annex 1 (F0133109), kept as a record of what was prepared for
filing (Rule 15). Key (`UserId`, `Year`, `Quarter`, `Type`).

Fields: `Type: Reporting | NewReporting | Clarifying`, `FileName` (the DPS name, standard No. 729),
`Content` (the windows-1251 bytes as generated and validated), `AnnexFileName?` and `AnnexContent?` (the
annex the same way, both set or both null), `GeneratedAt`. Preparing the same quarter and type again
replaces the row, annex included. Not audited: it is derived from audited records. Relationships: belongs
to `User`.

---

### DeclarationFigures (computed)

Responsibilities: the group 3 lines of one quarter's declaration (Rule 15). Not stored; read off the
year's accruals.

Fields: `Year`, `Quarter`, `IncomeKop` (06, up to the limit), `ExcessIncomeKop` (07, over the limit),
`TotalIncomeKop` (08), `SingleTaxKop` (11), `ExcessTaxKop` (09), `TotalSingleTaxKop` (12),
`PreviousSingleTaxKop` (13), `SingleTaxPayableKop` (14.1, 14), `MilitaryLevyKop` (23),
`PreviousMilitaryLevyKop` (24), `MilitaryLevyPayableKop` (25), `EsvKop?` (21, Q4 only). The payable
lines are negative after a refund that shrank the cumulative income. Lines 07 and 09 are nonzero only
in the quarter the limit is crossed in; a quarter outside group 3 after a crossing, in that year or a
later one, has no group 3 declaration. After a return to group 3 the period, and so lines 13 and 24,
start from the quarter of the return.

---

### LimitCrossing (computed)

Responsibilities: says that the income went over the year's limit (Rule 4) and where group 3 ends.
Not stored; computed by `Accruals.ForYears`, which runs the years in order and hands each one the
crossing the year before left group 3 stopped by.

Fields: `Year`, `Quarter` (the quarter the limit was crossed in, the last one accrued),
`SwitchFromYear`, `SwitchFromQuarter` (the next quarter, the next year's Q1 after a Q4 crossing). A
year's accrual names the crossing of its own year, or else the earlier one that keeps a quarter of it
out of group 3; its accruals, obligations, advances, reserve and declarations hold only the quarters
in group 3, and `StoppedAtYearEnd` is the crossing the next year inherits. The API adds
`BackOnGroup3From` when the owner set one after the crossing.

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
- `Group3ApplicationDays` (10, calendar days after registration to apply for group 3 from the
  registration date, Tax Code 298.1.2). The registration year's value applies.
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
TooManyInOneSecond | Unexpected | NbuRateUnavailable`; both set or both null, cleared when a window of the account imports). Unique per
(`UserId`, `Bank`, `ExternalId`).

Settings shows, per followed account, the month its cursor has reached (or that the history is
imported, once `HistoryImportedAt` is set), its latest `ImportBatch` as the last sync, and its last
failure.

A row exists for every account the token exposed, FOP or not, so settings can list an unsupported
type without a second call to the bank. Only a `fop` account can ever have `IsActive = true`.

Sync health (Rule 18) is derived from these fields and the connection's `RejectedAt`, never stored:
`SyncedThrough` of the accounts with `HistoryImportedAt` set is the last successful sync, and
`LastFailure = TokenUnreadable` is an unreadable token. The dashboard and the incident alerts read it
through one function, so they cannot disagree.

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

### ReserveJar

Responsibilities: the monobank jar one owner keeps the tax reserve in (Rule 13). At most one row per
owner, key `UserId`. Not audited, because it is rewritten by every sync and holds the owner's savings.

Fields: `JarId` (the jar's id in `client-info`, up to 100 characters), `Title` (the jar's name as the
bank last reported it, up to 200), `BalanceKop` (an integer, never negative), `FetchedAt` (when the
bank reported that balance; a reused answer keeps its own time). Only a jar with the hryvnia currency
code is ever stored. A refresh updates the title, balance and time of the row it read, never a row
that was changed, cleared or restored meanwhile; a failed or skipped refresh leaves the row as it was.

The jars are read by one path: `client-info` through `MonobankClient` and the rate gate's `client-info`
slot. `MonobankClientInfoReader` holds the last whole answer (name and jars) in memory for one gate interval, so
listing the jars, choosing one, refreshing and the invoicing prefill take one bank call between them; the answer is never stored and never
holds the token. The row is returned only by the owner's own endpoints, which are the Settings
section, the refresh button and the dashboard's reserve card; none of them returns the token.

Relationships: belongs to `User`, 1-to-1. Carried in the backup from schema version 14. Disconnecting
monobank leaves it.

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
- `ClientId?`, `Description`, `Counterparty`.
- `InvoiceId?` the issued invoice this receipt pays (#93): set only by linking, only on a confirmed
  `Income` row in the invoice's currency, to the same owner's issued invoice, of any client (an intermediary may pay; the receipt keeps its client, and one
  without a client takes the invoice's when linked). A receipt pays at most one
  invoice. `InvoiceNumber?` is free text on an unlinked receipt; linking overwrites it with the invoice's
  number and unlinking clears it but keeps an adopted client. While linked, the receipt keeps its kind, currency and number (an edit
  changing them is refused). Deleting a linked receipt removes the link with it; dismissing an imported
  one clears both fields.
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
Rule 7's allocation, oldest debt of its kind first, whatever group 3 quarter or month it names. A
payment naming a quarter outside group 3 (Rule 4) is not allocated at all: the engine lists it on its
kind's ledger as `OutsideGroup3`, and the year's balances show it apart.

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
`CounterEdrpou?` (the counterparty's code as the bank sent it, up to 10 characters, kept so a confirmation can
teach a `TreasuryAccount` its recipient code), `CreatedAt`, `ResolvedAt?`. The suggested kind and period are not stored: they are read off the
learned kinds, the purpose and the current ledger each time the list is shown.

Relationships: belongs to `User` and `BankAccount`.

---

### TreasuryAccount

Responsibilities: where the owner pays one kind of tax (#98, Rule 12): the account a Pay panel will show.
One row per `User` and `Kind` (`SingleTax | MilitaryLevy | Esv`), created on the first confirmation or manual
entry. Audited.

Fields: the Manual details, all four set or none: `ManualIban`, `ManualRecipientName`, `ManualRecipientCode`
(8 digits), `ManualUpdatedAt`; the Learned details, `LearnedIban`, `LearnedRecipientName?`,
`LearnedRecipientCode?`, and the operation they came from, `LearnedExternalId`, `LearnedPaidOn`, `LearnedAt`,
set together or not at all (a name or code needs the IBAN); they are the latest confirmed operation of the
kind by payment date, rebuilt from the remaining confirmations when that operation's payment is deleted or
changes kind; `NoticeAt?`, set while the Learned IBAN differs from the Manual account since a confirmation
made it so and the owner has not dismissed it (needs both a Manual and a Learned account). The account in use is
the Manual one when present, else the Learned one; the API reports it with its source
(`None | Learned | Manual`) and the recipient details it lacks.

Validity (#173, Rule 16): each account may carry an end, `ManualValidUntil?` and `LearnedValidUntil?`, a
`DateOnly`, the last day it can receive a payment. An end needs its account (a Manual end needs the Manual
account, a Learned end the Learned IBAN). The Manual end is entered with the Manual account or set later; the
Learned end is only ever set by the owner and belongs to the Learned IBAN, so it goes when another IBAN becomes
the Learned account and stays when the same IBAN is learned again. Reverting to Learned drops the Manual end.
The API reports the end of the account in use as `ValidUntil`. Entering a Manual account without an end keeps
the end only for the same IBAN (its own, or the Learned account's), else none.

Relationships: belongs to `User`. Written by confirming a `BudgetPaymentCandidate`, by deleting or retyping its
payment, by a sync that fills its candidate's code, and by the settings screen.

---

### Payment details

Responsibilities: what the Pay panel shows for one obligation (#99, Rule 16). A derived value, not
stored and not audited: computed on request from the owner's `TreasuryAccount` of the kind.

Fields: `Kind`, `PeriodYear`, `PeriodQuarter?` or `PeriodMonth?` (exactly one), `AmountKop?` (the query's amount, optional: without it the
recipient, purpose and `Missing` still come, so the panel reads them once whatever is typed, and `QrContent`
is null), `Purpose`
(Rule 16), `Recipient?` (IBAN, name, code and the account's source, `Learned | Manual`) and `Missing`
(`iban`, `recipientName`, `recipientCode`, in that order). `Recipient` is set exactly when `Missing` is
empty. `QrContent?` is the NBU QR of the same details (#100, Rule 16), set only with a `Recipient` and
only when the details fit format 003.

Relationships: read from `TreasuryAccount` and the year's `TaxYearConfig`; belongs to `User`.

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
`TotalKop` (the dues added). Against a stored `ReserveJar` balance the engine's `TaxReserve.Cover`
answers `SurplusKop` or `ShortfallKop` (at most one is above zero) and, when short, `TopUpBy` with
`TopUpKop` (Rule 13, the reserve jar); the dashboard returns these under `Reserve.Jar` with the jar's
name, balance and `FetchedAt`.

---

### FxRate

Responsibilities: a cache of NBU exchange rates.

Fields: `Currency`, `Date` (the requested date), `RateE4`, `RateDate` (the NBU date the rate belongs
to, earlier than `Date` after a fallback), `FetchedAt`. Key (`Currency`, `Date`). A future `Date` is
never cached.

---

### Backup file

Responsibilities: one owner's data as a JSON file to download and restore. Not stored.

Fields: `SchemaVersion` (16), `Settings?`, `Clients`, `Transactions`, `BudgetPayments`,
`BankAccounts`, `ImportBatches`, `BudgetPaymentCandidates`, `InvoicingDetails?` (with its per-currency
payment details and the signature as base64 with its content type), `Invoices` (with their lines, their
number as year and sequence, the frozen snapshot and the frozen signature as base64), `DeclarationDetails?`,
`DeclarationFilings`, `DeclarationFiles` (the XML and its annex as base64), `TreasuryAccounts` (by kind, without an id), `NotificationChannels` (kind, address, enabled, linked at, confirmed at: no delivery record, no link code, no confirmation token), `ReserveJar?` (jar id, name, balance and the time of the balance), each row with its id and every stored column except `UserId`, an invoice's
`TotalMinor` (recomputed from its lines) and a
bank account's sync state (`SyncedThrough`, `HistoryImportedAt`, `LastFailedAt`, `LastFailure`),
which a restore clears.
`TaxYearConfig` and `FxRate` are left out because they are shared. The monobank connection is never
in it, so the file carries no bank access. Version 2 added the bank accounts, the import batches and
the transactions' import fields (#76); version 3 added the budget payment candidates, all statuses,
and the payments' bank operation (#80); version 4 added the invoicing details (#91); version 5 added the
clients' details (#90); version 6 added the invoices (#92); version 7 added the declaration details and
the filed marks (#110); version 8 added the receipts' `InvoiceId` (#93); version 9 added the Treasury
accounts and the candidates' `CounterEdrpou` (#98); version 10 added the settings' `BackOnGroup3From` (#118); version 11 added the notification channels (#106); version 12 added the declaration files and the
declaration details' `TaxOfficeName` (#111); version 13 added the declaration files' annex (#112); version 14 added the reserve jar (#102); version 15 added the notification channels' `ConfirmedAt` (#107); version 16 added the settings'
group 3 status: `Group3Since`, the confirmation and the checklist ticks (#172); version 17 added the Treasury accounts' `ManualValidUntil` and `LearnedValidUntil` (#173); version 18 added the declaration details' `FullName`, `Phone` and `ReportEmail` (#222). A version 1 file still restores, read as having none of
them and every transaction `Confirmed`, a version 2 file as having no candidates and every payment typed by
the owner, a version 1 to 3 file as having no invoicing details, so the owner's are cleared like the rest, a
version 1 to 4 file as having no details on any client, a version 1 to 5 file as having no invoices, a
version 1 to 6 file as having no declaration details and nothing marked filed, a version 1 to 7 file as
having no receipt linked to an invoice, and a version 1 to 8 file as having no Treasury accounts and
candidates without a counterparty code, a version 1 to 9 file as having no return to group 3, and a version 1 to 10 file as having no notification channels, and a version 1 to 11 file as having no declaration
files and no tax office name, a version 1 to 12 file as having no annex on any declaration file, and a version 1 to 13 file as having no reserve jar, and a version 1 to 14 file as having its channels confirmed when they were linked (every channel before email is a Telegram chat), and a version 1 to 15 file as having group 3 from its registration date (null), unconfirmed, with no ticks, and its `Prorated` read as `FullMonth`, and a version 1 to 16 file as having no end on any Treasury account, and a version 7 to 17 file as having no full name, phone or email for reports in its declaration details; a file of a version this build does not know is refused by its
version number rather than by whichever field it added.

A restore replaces the owner's settings, invoicing details, declaration details, filed marks, declaration files, clients, invoices, transactions, payments, candidates, Treasury accounts, the reserve jar and import batches in one
database transaction and passes every row through the endpoints' own validation, refund and invoice links
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

Responsibilities: change log for transactions, clients, invoices, budget payments, Treasury accounts, settings and year parameters. One
row (`AuditEntry`, table `AuditLog`) per created, changed or deleted record. Append-only.

Fields: `Entity: Transaction | BudgetPayment | Settings | InvoicingDetails | TaxYearConfig | Backup | Client |
Invoice | DeclarationDetails | DeclarationFiling | TreasuryAccount | NotificationChannel`, `EntityId` (the record's key: a GUID, the year, the owner's
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

### NotificationChannel (Stage 2)

Responsibilities: where one owner's reminders go (#106, #107). One row per owner and kind: `Telegram` and
`Email`. Reminders are not stored ahead: `ReminderPlan` computes what is
due at each run (Rule 17) and `SentReminder` makes delivery idempotent, because a payment changes what is
owed between scheduling and sending. Every enabled channel with an available sender gets each reminder.

Fields: `Kind: Telegram | Email`, `Address` (the Telegram chat id as text, never sent to the browser; or
the email address, which is), `Enabled`, `LinkedAt`, `ConfirmedAt?`, `LastDeliveryAt?`, and `LastFailure?`
with `LastFailureAt?` (both set or both null; a later delivery clears them). `LastFailure: Blocked |
Rejected | RateLimited | Unreachable | Timeout | ServerError | Unreadable | Authentication`. Unique on
(`UserId`, `Kind`).

`ConfirmedAt` is null while an email address waits for its link; `Enabled` is never true before it is
set, and an unconfirmed address receives the confirmation email and nothing else (Rule 17). A Telegram
chat is confirmed by pressing Start, so its `ConfirmedAt` is set with `LinkedAt`. Adding a different
address replaces the row's address, clears `ConfirmedAt`, `Enabled` and the delivery record, and sends a
new confirmation. The confirmation link is stateless (ADR-022): nothing about it is stored.

A Telegram 403 (the owner blocked the bot) sets `Blocked` and `Enabled = false`; switching the channel
on again clears the failure. Audited like `Settings`, except `LastDeliveryAt`, `LastFailure` and
`LastFailureAt`, which change with every message and are left out of the snapshot. Carried in the backup
(kind, address, enabled, linked at) from schema version 11 and confirmed at from 15; a restore starts with a clean delivery record, and brings an email address back unconfirmed and off (a file proves nothing about a mailbox), so the owner sends the link again.

### SentReminder (Stage 2)

Responsibilities: the log of reminders already claimed for sending (#108), which is what makes Rule 17's
"at most once per channel" hold across restarts, redeploys and overlapping runs. Written by the reminder
worker only.

Fields: `UserId`, `Date` (the deadline or advance date the reminder is about), `Kinds` (flags:
`SingleTax`, `MilitaryLevy`, `Esv`, `Declaration`, `Group3Application`), `Offset` (days from `Date`: -7, -1, 0 or 1), `Channel`
(`NotificationChannelKind`), `Incident` (empty for a reminder), `ClaimedAt`, `DeliveredAt?`. Unique on
(`UserId`, `Date`, `Kinds`, `Offset`, `Channel`) among rows with no `Incident`.

An incident alert (Rule 18, ADR-026) is a row of the same log: `Incident` holds its key (the kind and the
Unix second the state began, such as `SyncStale:1790000000`), `Kinds` is none, `Offset` is `OnTheDay` and
`Date` is the day it was claimed. The December prompt for a new tax year (Rule 9) is such a row, keyed
`NewTaxYear:<year>`. It is unique on (`UserId`, `Incident`, `Channel`) among rows with an
`Incident`, so an incident is claimed once per channel however many days it lasts, and the same delivery
rules apply.

A row is inserted before the message is sent and gets `DeliveredAt` once the channel accepts it. A
failure that proves nothing was delivered (unreachable, rate limited, server error) deletes the row so a
later run retries; a permanent failure, a timeout (possibly delivered) or a crash mid-send leaves
it without `DeliveredAt`, and the message is not sent again. A run sends nothing for a date, offset and
channel whose rows already name every kind it would send.

Not audited and not in the backup: it records what was sent to a chat, not the owner's data, and a restore
never touches it, so a restore never resends. Only restoring into a fresh database starts without it; the cost is at most one repeat
of a reminder whose window is still open, since Rule 17 never replays a moment older than the latest one
passed, and a day-after reminder only on its day. Rows are never pruned: about a hundred a year per
channel.

### NotificationLinkCode, TelegramPollState (Stage 2)

`NotificationLinkCode`: the one-time code behind "Connect Telegram". `CodeHash` (SHA-256 of 32
random base64url characters, unique), `UserId`, `Kind`, `ExpiresAt` (15 minutes). Asking for a new code
deletes the owner's earlier ones; redeeming deletes the code whatever the outcome, so it works once;
disconnecting and restoring delete the owner's codes. Never audited, never in the backup, never logged.

`TelegramPollState`: `BotId` (the number before the colon in the token) and `NextOffset`, the
`getUpdates` offset. Keyed by bot because update ids belong to one bot. It moves in the same save as the
effect of the update it passes, so a restart neither replays nor skips one. Not audited, not in the backup.

### CalendarFeed (Stage 2)

Responsibilities: the secret behind one owner's calendar subscription URL (#105, ADR-017). Absent
until the owner asks for a link; asking again replaces it.

Fields: `UserId` (primary key), `Secret` (64 random hex characters, unique, the path segment of
`/api/calendar/feed/{secret}.ics`), `CreatedAt`. Never audited, never logged, never in the backup: a
restore creates none and leaves an existing one alone, so the owner rotates in settings to get one on a
new server. The deadlines it serves are computed from the engine at request time (Rule 5), so the row
stores nothing else.

Relationships: belongs to `User`, 1-to-1.

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
and payment term. An invoice with receipts linked cannot be cancelled (409) until they are unlinked.

Payment (#93): receipts link to an issued invoice (`Transaction.InvoiceId`). Derived on read and never
stored, the API returns `Standing: Draft | Issued | Overdue | Paid | Cancelled`, `PaidMinor` (the linked
receipts less the refunds linked to each) and `DueMinor` (the total less `PaidMinor`, null on a draft and
a cancelled invoice); an invoice also lists its receipts. Paid when `PaidMinor` reaches the total;
overdue when issued, not paid and today in Kyiv is after `DueDate` (Rule 14). The owner links from the
invoice, choosing among unlinked receipts in its currency of any client, newest first, with a warning on one whose client is
not the invoice's,
or from the receipt, choosing among the open invoices in its currency of any client, closest due date first, with
the same warning on one whose client is not the receipt's. The warning compares client ids, never names, so
renaming a client does not raise it. Unlinking keeps a client the receipt adopted on linking.

Suggested payment (#94): `GET /api/invoices/suggestions` answers, for each of the owner's receipts waiting
for review that fits an open invoice, the receipt's id and the matching invoices as summaries, closest due
date first. Nothing is stored: the match is `InvoiceMatcher` in `TaxesUa.Engine` (currency, amount equal to
`DueMinor`, invoice number or client name in the counterparty, description or comment), and linking is the
ordinary link of #93. Rule 14 states the rule.

Audited as `Invoice`: `Create`, `Update` (edits, issue, cancel) and `Delete` of a draft.

Relationships: belongs to `User` and `Client`, has many `Transaction` (the receipts paying it).
