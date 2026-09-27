# Business Rules

Source: the owner's spec and their 2026 reference table. A "to confirm" mark means the owner has
not yet confirmed the interpretation; the behavior is made configurable.

## Rule 1. What counts as income

Period income = sum of `Transaction.Kind = Income` minus sum of `Kind = RefundToClient`, by
`ValueDate`. A refund of a prepayment reduces the income of the period in which the refund
happens. A refund linked to the receipt it reverses follows that receipt's income treatment: if
the receipt is excluded, so is the refund (Rule 8).

Not income: transfers between one's own accounts, hryvnia from selling one's own currency,
exchange-rate differences, top-ups from one's own funds, refunds of erroneous payments. Every
such operation must carry a type and a reason. Expenses are not deductible.

Income arises on the credit date, so a transaction cannot be dated after today in Kyiv. A
`ValueDate` in the future is rejected with a field error, and the form does not let the owner pick
one.

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
- Registration month: prorated by active days by default (`EsvRegistrationMonthPolicy.Prorated`),
  confirmed by the owner. `EsvRegistrationMonthPolicy.FullMonth` stays available as a setting.
- ESV exemption (`Settings.EsvExempt`) zeroes out the ESV accrual.

The declaration is filed cumulatively. Quarter tax = tax on cumulative income minus tax already
accrued for prior quarters of the year.

---

## Rule 4. Income limit

Annual limit = `IncomeLimitMinWages` × minimum wage as of January 1 (2026: 10,091,049 UAH). The
limit is not prorated for a partial year, whatever the registration date or the length of the year
already elapsed. Income before `FopRegistrationDate` is excluded the same way it is from every
other accrual (Rule 8), so the limit bar never counts it.

Warnings at 85% and 100% of the limit: at or above 85% of the limit is `Warn`, at or above the
limit itself is `Exceeded`. Both boundaries are inclusive, so income at exactly 85.00% is already
`Warn` and income at exactly 100.00% is already `Exceeded`; one kopeck under either line stays at
the level below it. `Exceeded` is a business fact, not a display threshold: it marks that a
switch to another tax system is required, whatever warn thresholds happen to be configured.

`RemainingKop` is the amount left before the next boundary not yet crossed: while below 85%, the
amount left to 85%; from 85% up to the limit, the amount left to the limit; once `Exceeded`, 0
(the excess figures below apply instead).

Excess is taxed at `ExcessRateBp` (15%), computed on the excess over the limit only (not on the
whole income), with the same integer half-up rounding as every other tax figure (Rule 10).

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

`ShiftTaxPaymentFromWeekend` shifts off any non-working day, holidays included, not off weekends
only (to confirm). The flag is named for the weekend because that is the case the 2026 reference
exercises, but the paragraph above defines shifting as moving to the next *business* day and names
holidays in the same breath, so the payment deadline follows the same definition as the other two.
Its `false` value therefore also leaves a payment deadline sitting on a holiday. No payment date in
the 2026 reference falls on a weekend or a holiday, so the table cannot settle this.

Q4's deadlines fall in January and February of the following year, and they shift against the
holidays of the year the quarter belongs to, not the year the dates fall in (to confirm). Nothing
distinguishes the two readings today, because the martial-law holiday list is empty. It starts to
matter the first year holidays come back. `GET /api/periods/{year}` is built on this reading: it
passes the quarter year's `TaxYearConfig` for all four quarters, so a Q4 date is shifted only by a
holiday listed in that year's config. If the owner confirms the other reading, the endpoint is the
one place to change.

Whether the quarter *containing* `Settings.FopRegistrationDate` is shown at all is a display
decision, not a rule. The engine returns every quarter of the year; `GET /api/periods/{year}` omits
a quarter whose last day is before the registration date and keeps the quarter containing it, since
that quarter carries obligations.

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
- `MonthlyAdvance`: recommended monthly advance = the month's EP and VZ (the year-to-date
  difference defined below) + ESV for the month, recommended date `AdvanceRecommendedDay` (15th)
  of the following month. Advances are credited against the quarterly obligations.

The mode changes recommendations only, never an accrual or a balance: obligations stay quarterly,
and Rule 7 allocates every payment the same way in both modes.

A month's EP and VZ are the year-to-date tax through the month minus that through the month before,
the way the declaration splits quarters, so a quarter's three months add up to its accrual to the
kopeck; taxing the month's income alone could leave a one-kopeck remainder after every advance was
paid. A month's ESV is the month's ESV accrual (Rule 3).

The recommended advance is what the allocation left unpaid of the month's three accruals. Rule 7
settles each quarter first; what it paid on a quarter is then split over that quarter's months,
oldest month first, and a refund month's negative accrual is credit to the quarter's other months.
The months' remainders therefore add up to the quarter's remainder, and nothing already paid is
recommended again. A paid-up month recommends zero. The "By month" table (income, EP, VZ, ESV, the
advance and its date) is shown in this mode only.

On the home screen, in this mode, a kind with nothing yet fallen due shows the advance instead of
the quarter when the advance's date comes before the quarter's deadline: the first of the nearest
open quarter's advances dated today or later that has anything left, with the unpaid remainder of
the quarter's earlier months added to it, so a missed advance is caught up rather than dropped. An
advance is a recommendation, so it is never overdue. Once the quarter's last advance date has passed,
the quarterly deadline is the step again. Anything already overdue is the step in both modes.

---

## Rule 7. Payment balances

Per kind (EP, VZ, ESV) independently: accrued cumulatively minus paid = owed or overpaid. Kinds are
never mixed: an overpayment of one kind never reduces another kind's debt.

Within a kind, money paid settles the oldest outstanding obligation first, by due date, whatever
quarter or month the payment names (owner decision 2026-09-27; this is how the tax office credits
payments against debt, Tax Code art. 87.9). Q1 levy 1,000.00 unpaid, Q2 levy 1,000.00 accrued and
2,000.00 paid "for Q2" leaves both quarters settled. The period a payment names is kept and shown;
it no longer decides which obligation the payment settles. A refund that turns a quarter's accrual
negative adds that amount to the kind's credit in the same way.

The allocation runs across years (owner decision 2026-09-27). An unpaid balance of year Y is still
owed, and still overdue, in Y+1; an overpayment at the end of Y settles Y+1's obligations of the same
kind. A year's figures follow the allocation, not the period payments name: paid is what was
allocated to that year's quarters, owed is what every quarter up to and including that year still
owes, and an overpayment is shown only when the kind's whole ledger is in credit. A year whose debt a
later-named payment settled reads as settled.

The ledger always starts at the registration year, whichever year is viewed; payments named for an
earlier year are not counted, and a year before registration shows no obligations. It runs through
consecutive configured tax years and stops at the first year without one, because that year's
accruals are unknown: a year after the gap shows no obligations until the missing year is
configured. Both cases say so on screen: the api names the reason (year before registration, or the
missing year) as a warning, never as text.

The home screen shows the next step from the allocated ledger, never from a pooled figure. Per kind
it names one debt, across years: what every open obligation that has fallen due still owes, dated
from the oldest of them; when nothing has fallen due, the nearest open quarter alone, since later
quarters' ESV accrues up front but is not owed by that date. The step is every debt already due or
overdue, or else the debts sharing the nearest date. EP and VZ share a deadline, so they come
together, each with its own amount and no total. A payment recorded from the home screen names the
oldest open quarter it covers; allocation would settle the oldest first anyway. The tax burden
beside it divides all three kinds' accruals by income, year to date through the current quarter. It
is a statistic and not a balance, which is why it is the one place the kinds are added.

---

## Rule 8. FOP registration

Before `FopRegistrationDate` there are no obligations. Operations with a `ValueDate` earlier than
the registration date are flagged with a warning and excluded from income.

The exclusion is transitive: a refund linked to an excluded receipt is excluded too, whatever its
own date, so it neither lowers period income nor creates a tax credit. An unlinked refund is judged
by its own date only.

Advice for the owner: file the Group 3 application together with the registration, so the single
tax applies from the registration date. Otherwise the general tax system applies until the 1st of
the following month.

A budget payment whose `PaidOn` is before `FopRegistrationDate` is a different case: it is still
saved and credited toward its kind's balance (unlike a receipt, a payment is never excluded), and
the payments list only shows a soft warning on that row so the owner can double-check the date.

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
