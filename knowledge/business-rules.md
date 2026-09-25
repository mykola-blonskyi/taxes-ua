# Business Rules

Source: the owner's spec and their 2026 reference table. A "to confirm" mark means the owner has
not yet confirmed the interpretation; the behavior is made configurable.

## Rule 1. What counts as income

Period income = sum of `Transaction.Kind = Income` minus sum of `Kind = RefundToClient`, by
`ValueDate`. A refund of a prepayment reduces the income of the period in which the refund
happens.

Not income: transfers between one's own accounts, hryvnia from selling one's own currency,
exchange-rate differences, top-ups from one's own funds, refunds of erroneous payments. Every
such operation must carry a type and a reason. Expenses are not deductible.

---

## Rule 2. Currency and exchange rate

A foreign-currency receipt is converted at the official NBU rate on the date it is credited to
the currency account (`ValueDate`). The rate is fixed at the time of recording and never changes
afterward. A manual rate correction is allowed; the source is then marked `Manual`.

NBU does not publish a rate on weekends. The rate of the last business day is used, and the
actual rate date is stored in `RateDate`.

Formula: `AmountUahKop = roundHalfUp(AmountMinor × RateE4 / 10000)`.

---

## Rule 3. Rates and ESV

- Single Tax: `SingleTaxRateBp` of income (2026: 5%).
- Military Levy: `MilitaryLevyRateBp` of income (2026: 1%).
- ESV for oneself: `EsvRateBp` of the monthly minimum wage (2026: 22% × 8,647 = 1,902.34 UAH).
  Paid from the month of FOP registration, regardless of income.
- Registration month: full amount by default. Setting `EsvRegistrationMonthPolicy`. To confirm.
- ESV exemption (`Settings.EsvExempt`) zeroes out the ESV accrual.

The declaration is filed cumulatively. Quarter tax = tax on cumulative income minus tax already
accrued for prior quarters of the year.

---

## Rule 4. Income limit

Annual limit = `IncomeLimitMinWages` × minimum wage as of January 1 (2026: 10,091,049 UAH). The
limit is not prorated for a partial year. Warnings at 85% and 100%. Excess is taxed at
`ExcessRateBp` (15%) and requires switching to another tax system.

---

## Rule 5. Deadlines

All deadlines are quarterly.

- ESV: by `EsvDeadlineDay` (19th), inclusive, of the month after the quarter.
- Declaration: `DeclarationDays` (40) calendar days after the end of the quarter.
- EP and VZ: `TaxPaymentDaysAfterDeclaration` (10) days after the declaration's statutory
  deadline.

Shifting: if the ESV or declaration deadline falls on a weekend, it moves to the next business
day. Weekends are `Settings.WeekendDays`; holidays come from `TaxYearConfig.Holidays`. During
martial law, holidays are treated as business days, so the holiday list is empty.

The EP/VZ payment deadline is counted from the declaration's statutory (unshifted) date
(`TaxPaymentCountsFromStatutoryDeclarationDate`, to confirm). The payment deadline itself is also
shifted off a weekend (`ShiftTaxPaymentFromWeekend`, to confirm).

2026 reference (ESV / declaration / tax):

| Quarter | ESV | Declaration | EP and VZ |
| --- | --- | --- | --- |
| Q1 | 2026-04-20 | 2026-05-11 | 2026-05-20 |
| Q2 | 2026-07-20 | 2026-08-10 | 2026-08-19 |
| Q3 | 2026-10-19 | 2026-11-09 | 2026-11-19 |
| Q4 | 2027-01-19 | 2027-02-09 | 2027-02-19 |

The annual declaration (for Q4) includes the ESV attachment.

---

## Rule 6. Payment modes

- `Quarterly`: payments on the official deadlines.
- `MonthlyAdvance`: recommended monthly advance = EP + VZ on the month's income + ESV for the
  month, recommended date `AdvanceRecommendedDay` (15th) of the following month. Advances are
  credited against the quarterly obligations.

---

## Rule 7. Payment balances

Per kind (EP, VZ, ESV) independently: accrued cumulatively minus paid = owed or overpaid. An
overpayment carries forward to the next period of the same kind. Kinds are never mixed.

---

## Rule 8. FOP registration

Before `FopRegistrationDate` there are no obligations. Operations with a `ValueDate` earlier than
the registration date are flagged with a warning and excluded from income.

Advice for the owner: file the Group 3 application together with the registration, so the single
tax applies from the registration date. Otherwise the general tax system applies until the 1st of
the following month.

---

## Rule 9. Year parameters

Every year has a `TaxYearConfig` row. If the current year has no row, or its `VerifiedAt` is
empty, the interface shows a warning. Values are never hardcoded in the code.

---

## Rule 10. Money and dates

No floats. Whole kopecks, one rounding per operation, half away from zero. Operation dates are by
Europe/Kyiv; bank UTC timestamps are converted at the boundary.

---

## Rule 11. Disclaimer

The calculation is informational. The user reconciles accruals against the Electronic Cabinet.
The application does not pay taxes and does not file declarations.
