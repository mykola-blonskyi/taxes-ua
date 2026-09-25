# Domain Model

Деньги: `long` в минимальных единицах валюты (копейки, центы). Курс: `int RateE4` = курс × 10⁴.
Проценты: базисные пункты (`int`, 5% = 500). Даты операций: `DateOnly` по Europe/Kyiv.
Все сущности кроме `TaxYearConfig` имеют `UserId`.

## Entities

### User

Responsibilities: владелец данных. Identity‑пользователь ASP.NET Core.

Fields: `Id`, `Email`, `DisplayName`, `CreatedAt`. Passkeys и внешние логины хранит Identity.

Relationships: 1‑к‑1 `Settings`, 1‑ко‑многим всё остальное.

---

### Settings

Responsibilities: параметры конкретного ФОП, влияющие на расчёт.

Fields:

- `FopRegistrationDate: DateOnly?` дата регистрации ФОП. Пока не задана, обязательств нет.
- `PaymentMode: Quarterly | MonthlyAdvance`.
- `EsvRegistrationMonthPolicy: FullMonth | Prorated` по умолчанию `FullMonth`.
- `EsvExempt: bool` освобождение от ЕСВ за себя (например, платит работодатель). По умолчанию `false`.
- `TaxPaymentCountsFromStatutoryDeclarationDate: bool` по умолчанию `true`.
- `ShiftTaxPaymentFromWeekend: bool` по умолчанию `true`.
- `WeekendDays: DayOfWeek[]` по умолчанию суббота и воскресенье.
- `Locale`, `Theme`, `DefaultCurrency`.

Relationships: принадлежит `User`.

---

### TaxYearConfig

Responsibilities: все параметры налогового года. Один источник правды для движка. Без `UserId`.

Fields:

- `Year`.
- `MinWageKop` минимальная зарплата на 1 января.
- `SingleTaxRateBp` (500), `MilitaryLevyRateBp` (100), `EsvRateBp` (2200), `ExcessRateBp` (1500).
- `EsvMonthlyKop` вычисляется как `MinWageKop × EsvRateBp / 10000`, хранится для прозрачности.
- `IncomeLimitMinWages` (1167), `IncomeLimitKop` вычисляется и хранится.
- `LimitWarnThresholdsPct` (85, 100).
- `EsvDeadlineDay` (19, включительно, месяц после квартала).
- `DeclarationDays` (40, календарных после конца квартала).
- `TaxPaymentDaysAfterDeclaration` (10).
- `AdvanceRecommendedDay` (15, число следующего месяца).
- `Holidays: DateOnly[]` нерабочие праздники. На время военного положения пусто.
- `Source` ссылка на норму, `VerifiedAt: DateTime?`.

Relationships: нет. Движок получает список конфигов на входе.

---

### BankAccount

Responsibilities: счёт ФОП или личный счёт, источник импорта.

Fields: `Bank: Monobank | PrivatBank | Other`, `Name`, `Currency`, `Iban`, `IsFop`, `ExternalId`,
`EncryptedToken?`, `IsActive`.

Relationships: принадлежит `User`, имеет много `Transaction`.

---

### Client

Responsibilities: контрагент для привязки поступлений и инвойсов.

Fields: `Name`, `Country`, `Address`, `Email`, `VatId`, `DefaultCurrency`, `Notes`. В MVP используется только `Name`.

Relationships: принадлежит `User`, имеет много `Transaction` и `Invoice`.

---

### Transaction

Responsibilities: одно движение по счёту с типом и гривневым эквивалентом.

Fields:

- `ValueDate: DateOnly` дата зачисления по Киеву. Определяет период.
- `BankTime: DateTimeOffset?` исходное время банка.
- `AmountMinor: long`, `Currency` (UAH, USD, EUR).
- `RateE4: int` (10000 для UAH), `RateDate: DateOnly` фактическая дата курса НБУ, `RateSource: Nbu | Manual`.
- `AmountUahKop: long` = roundHalfUp(`AmountMinor` × `RateE4` / 10⁴). Фиксируется при записи.
- `Kind: Income | RefundToClient | OwnTransfer | FxSale | OwnDeposit | ErroneousReturn | OtherNonIncome`.
- `NonIncomeReason?` текст для не‑дохода.
- `ClientId?`, `InvoiceId?`, `InvoiceNumber?`, `Description`, `Counterparty`.
- `ExternalId?` ID транзакции в банке, уникален в паре с `BankAccountId`.
- `ImportBatchId?`, `ReviewStatus: Confirmed | NeedsReview`.
- `CreatedAt`, `UpdatedAt`.

Правило: в доход периода входят `Income` со знаком плюс и `RefundToClient` со знаком минус.

Relationships: принадлежит `User`, опционально `BankAccount`, `Client`, `Invoice`, `ImportBatch`.

---

### BudgetPayment

Responsibilities: фактический платёж в бюджет.

Fields: `PaidOn: DateOnly`, `Kind: SingleTax | MilitaryLevy | Esv`, `AmountKop`, `PeriodYear`,
`PeriodQuarter?` (1–4), `PeriodMonth?` (1–12, для авансов), `Note`, `CreatedAt`.

Relationships: принадлежит `User`.

---

### Obligation (вычисляемая)

Responsibilities: что и когда должно быть сделано. Не хранится, считается движком.

Fields: `Year`, `Quarter`, `Kind: SingleTax | MilitaryLevy | Esv | Declaration | MonthlyAdvance`,
`Month?`, `AccruedKop`, `StatutoryDate`, `DueDate` (с переносом), `PaidKop`, `BalanceKop`
(плюс долг, минус переплата), `Status: Upcoming | Due | Overdue | Done`, `CumulativeIncomeKop`
для декларации.

---

### FxRate

Responsibilities: кэш курсов НБУ.

Fields: `Currency`, `Date`, `RateE4`, `FetchedAt`. Ключ (`Currency`, `Date`).

---

### AuditLog

Responsibilities: журнал изменений транзакций, платежей и настроек.

Fields: `Entity`, `EntityId`, `Action: Create | Update | Delete`, `Before: jsonb`, `After: jsonb`,
`At`, `UserId`.

---

### NotificationChannel, Reminder (Этап 2)

`NotificationChannel`: `Kind: Telegram | Email`, `Address` (chat id или email), `Enabled`.
`Reminder`: `ObligationKey`, `OffsetDays` (7, 1, 0), `ScheduledAt`, `SentAt?`, `ChannelId`, `Status`.

### ImportBatch (Этап 2)

`Source: Csv | Monobank | PrivatBank`, `BankAccountId`, `FromDate`, `ToDate`, `FileName?`,
`ImportedCount`, `SkippedCount`, `CreatedAt`.

### Invoice (Этап 3)

`Number`, `ClientId`, `IssueDate`, `DueDate`, `Currency`, `AmountMinor`, `Items: jsonb`,
`Status: Draft | Sent | Paid`, `PdfPath`.
