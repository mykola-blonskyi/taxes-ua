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
- Registration month: the full minimum (`EsvRegistrationMonthPolicy.FullMonth`, the default), whatever
  the registration day. Law 2464-VI sets the ESV of a FOP on the simplified system at no less than the
  minimum insurance contribution ("сума єдиного внеску не може бути меншою за розмір мінімального
  страхового внеску", art. 7 part 1 item 3, with the rate in art. 8 and the monthly payment in art. 9)
  and has no provision that reduces it for a part month. The DPS and the accounting press say the
  same: a FOP registered on 28 September 2026 owes 1,902.34 UAH for September. Sources:
  https://zakon.rada.gov.ua/laws/show/2464-17,
  https://lv.tax.gov.ua/media-ark/news-ark/print-397369.html,
  https://taxer.ua/uk/kb/splata-esv-dlia-novoho-fop,
  https://7eminar.ua/news/6368-ci-platit-fop-jesv-jedinii-podatok-ta-viiskovii-zbir-u.
- Until 2026-10-02 the default was `Prorated`, recorded as confirmed by the owner on 2026-09-27. That
  rested on the premise that the law allows a part-month minimum, which it does not. A migration moved
  every owner on `Prorated` to `FullMonth`, and a restore from backup reads `Prorated` as `FullMonth`
  (ADR-018, amendment of 2026-10-02).
- `Prorated` stays as a setting the interface labels as not matching the law. It prorates the base,
  the minimum wage times active days over the month's days, rounded once; the ESV is the rate on that
  base, rounded once, so annex 1's column 4 is column 2 times column 3 (Rule 15). This order (base ×
  rate) can differ by 1 kopeck from prorating the month's full ESV instead: with the 2026 minimum wage,
  registration on 5 April gives 1,648.70 UAH (not 1,648.69) and on 12 April 1,204.81 UAH (not
  1,204.82).
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
the level below it. `Exceeded` is a business fact, not a display threshold: it marks that the limit is used up, whatever
warn thresholds happen to be configured, and the switch to another tax system follows once income is
over it (crossing, below).

`RemainingKop` is the amount left before the next boundary not yet crossed: while below 85%, the
amount left to 85%; from 85% up to the limit, the amount left to the limit; once `Exceeded`, 0
(the excess figures below apply instead).

Excess is taxed at `ExcessRateBp` (15%), computed on the excess over the limit only (not on the
whole income), with the same integer half-up rounding as every other tax figure (Rule 10).

Crossing (owner decision 2026-09-30, #118). The crossing quarter is the first quarter whose cumulative
income at its end is over the limit; income exactly at the limit has no excess and crosses nothing (Tax
Code art. 291.4 allows income that does not exceed it). In that quarter the excess tax accrues as single
tax: the year-to-date single tax is `SingleTaxRateBp` on the income up to the limit plus `ExcessRateBp`
on the income above it, each rounded once, and the quarter's accrual is that minus what earlier quarters
accrued, as in Rule 3. It is owed with that quarter's single tax, by the same deadline (Rule 5), and
Rule 7 settles it in the single tax ledger: it is a single tax at another rate (Tax Code art. 293.4),
paid to the same budget account under the same code, so the Treasury cannot tell a payment of it apart
and a fourth kind would ask the owner to split what the bank never splits. The military levy stays on
the whole income, the excess included (the declaration's line 23 covers lines 05 to 07). Months follow
the same year-to-date function, so Rule 6's advances of the crossing quarter carry the excess and still
add up to the quarter.

From the next quarter on, group 3 no longer applies and the FOP must move to the general system or
another group. The engine accrues nothing for those quarters, no ESV either, rather than group 3
figures that would be wrong: their obligations, advances, reserve and declaration are absent, and
every screen that would show them says the FOP must switch from that quarter instead, naming the
crossing quarter and the quarter the switch starts from. The warning also says plainly that ESV and
the general system's taxes are still owed for that period; the app only does not compute them. A
payment the owner records for such a quarter, or for a month of one, is kept and listed apart from the
group 3 balances, and never settles a group 3 obligation (Rule 7). The stop runs across years: after a Q4 crossing the switch starts with the next year's Q1, after a Q1 to
Q3 crossing with the next quarter, and in both cases every later configured year is outside group 3
too, whatever its own income. A year missing from the configured run passes the stop on.

Only the owner lifts the stop, with the setting "back on group 3 from" a year and quarter. From that
quarter on the app computes group 3 again, as a new period: its income, the limit test, the
cumulative figures and the declaration's lines 13 and 24 start from zero in that quarter, since a
declaration period for a return to group 3 runs from the quarter of the return, and ESV accrues
from that quarter too. A setting at or before the crossing quarter lifts nothing; after a later
crossing the stop is back and the same single setting no longer lifts it. Without the setting
nothing after the crossing is computed.

A refund does not undo a crossing. Within the crossing quarter it counts as usual, so a refund that
brings the quarter's cumulative income back to the limit or under means the quarter never crossed.
Once a quarter has ended over the limit, the switch is due from the next quarter whatever later refunds
do, so a refund dated after the crossing quarter changes neither the crossing nor its figures; like
every operation of a quarter outside group 3, it falls in a period the app does not compute. The limit
bar stops at the crossing quarter too: income after it is not group 3 income, so the bar's excess and
excess tax are the ones accrued and owed.

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

The year's last group 3 declaration (Q4, or the crossing quarter's after a limit crossing) includes
the ESV annex (annex 1, Rule 15).

**Calendar feed.** The owner's deadlines are also served as an iCalendar document (ADR-017), for
every deadline dated in the current or the next calendar year (so last year's Q4 and December advance
stay until their dates pass): for each quarter still in group 3 (Rule 4, so none after a limit crossing
until the owner is back) and not ended before the registration date, the ESV date (not when `EsvExempt` is set), the single tax and
military levy date (one event, as they share it) and the declaration date, all as shifted above; in
`MonthlyAdvance` mode, and only for a year in the ledger as the periods screen shows it, also each
month's recommended advance date (Rule 6), unshifted. A year without a
`TaxYearConfig` has no events. Events are all-day, carry alarms at 09:00 local 7 and 1 days before, and name the kind
and the period in the owner's locale, never an amount. The UID is the kind with the year, the quarter or
month and a short non-secret owner key, so a moved date updates the event. The document is behind a per-owner secret path the owner
can rotate; it is not in the backup, so a restore creates none.

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
group 3 quarter or month the payment names (owner decision 2026-09-27; this is how the tax office credits
payments against debt, Tax Code art. 87.9). Q1 levy 1,000.00 unpaid, Q2 levy 1,000.00 accrued and
2,000.00 paid "for Q2" leaves both quarters settled. The period a payment names is kept and shown;
it no longer decides which obligation the payment settles. A refund that turns a quarter's accrual
negative adds that amount to the kind's credit in the same way.

A payment that names a quarter outside group 3 (Rule 4), or a month of one, stays out of the kind's
ledger (owner decision 2026-10-01, #125). It is not paid, not credit, and settles nothing in any year:
its money paid for a system the app does not compute, so pooling it would let it clear a resumed
quarter's obligation, or an older group 3 debt, that it never paid. It is still kept and shown: the
year's balances list it separately with its kind, period and amount, and no figure beside the list
counts it. Beyond the year's view a payment belongs to, this is all the named period decides. Every
payment naming a group 3 quarter,
the crossing quarter included, is allocated oldest first as above. Which quarters are outside group 3
follows the "back on group 3 from" setting, so moving it moves payments in or out of the ledger. A
FOP who never crossed the limit has no quarter outside group 3, so nothing changes for them.

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

---

## Rule 12. Bank import

A sync reads every followed FOP account forward in consecutive statement windows of 31 days (#77),
from `min(cursor, now − 31 days)` to now. The cursor is the end of the last window whose rows are
committed, written in the same database transaction as those rows, so an interrupted sync, a restart or
a redeploy resumes from it with nothing imported twice and nothing skipped. An account with no cursor
starts from the FOP registration date (Kyiv midnight), or from 1 January of the current year when
settings have none, and settings says which it is. The start is read only while the account has no
cursor: setting an earlier registration date later does not re-read the months before the cursor.
After the backfill, every sync is the last 31 days. The next window starts at the second the previous
one ended; an operation on that boundary is read twice and recorded once.

A sync is queued by "sync now", by saving a token or following an account, by the bank's webhook
and by the nightly run. The webhook (ADR-012) is a signal only: a POST to the owner's secret path
queues a sync of every followed account and its body is never read, so an operation is recorded only
from the statement the sync reads with the owner's token. The queue holds at most one waiting copy
of an account, so a burst of notifications costs at most one sync beyond the running one. An
unknown secret gets 404 and queues nothing. The webhook is registered only when a public base URL is
configured; its registration never blocks saving a token, and a failure is shown in settings. The
nightly run, at 03:00 in Kyiv, queues every followed account of every connection whose token was not
rejected, whatever its cursor says, so an operation no webhook announced is imported by morning.

An account's history counts as imported from the moment a window reaching the present is first
committed, and stays so however old the cursor grows (a missed night, a bank outage). Settings
shows "history loading, reached <month>" only before that. A restore clears the mark with the
cursor.

The sync queue lives in memory, so on start the app queues again every followed account whose cursor
is missing or older than 31 days, unless its owner's token was rejected. A restore clears every
account's cursor, history mark and last failure, since the restored transactions replace the synced ones, and queues
the followed accounts, which walk again from the start and record only what the file lacks. Each
window commits only while the walk still holds: the cursor is still where the walk left it, the
account is still followed, and the connection still has the token the walk read and is not rejected.
Otherwise the window writes nothing and the walk stops, so a restore, an unfollow, a disconnect or a
token replacement mid-backfill never leaves a gap behind a cursor or imports with a removed token.

Every statement call waits for the owner's rate gate: one call per 60 seconds. A 429 waits for the
bank's `Retry-After` when it sends one (at most 5 minutes, since one worker serves every owner), then
for the gate, and retries the same window; the owner sees nothing. After five 429s in a row on one
window the sync stops and records the failure, so a bank that keeps refusing cannot hold the worker. A 401 or 403
marks the connection's token rejected: that owner's queued and later syncs do nothing, settings asks
for a new token, and saving one clears the mark and queues the followed accounts, which resume from
their cursors. Saving a token, replaced or not, and following an account queue the followed accounts the
same way, so a walk stopped by a replacement, a reconnect or an unfollow picks up again. Any other failure (monobank unreachable or slow, an error status, an unreadable answer,
a token that cannot be decrypted, a window with 500 or more operations in one second, which the bank's
paging cannot split and which is therefore not committed, an unexpected error) is kept on the account with its time and reason
and shown in settings until a window of that account is imported again.

It records an operation as a transaction only when it is a credit (`amount > 0`) that the bank
reports settled (`hold` is false), in UAH, USD or EUR. A held credit waits for a later sync, so a
reversed authorisation never counts as income. A debit is never a transaction; a settled debit to the
Treasury becomes a budget payment candidate (below) and every other debit is ignored.

An imported credit goes through the same recording as a manual entry: Rule 1's no-future-date check,
Rule 2's NBU rate on the credit date, fixed at write time, and the client found or created by the
counterparty's name. Its `ValueDate` is the Kyiv calendar date of the bank's instant, so a credit at
23:30 UTC lands on the next day in Kyiv (Rule 10). It starts as `NeedsReview` with a suggested kind
(#78):

- `FxSale` for a UAH credit that pairs with a settled foreign-currency debit on one of the owner's
  own FOP accounts: within 60 seconds of it, and worth the debit at that day's NBU rate within 5
  percent. Both legs of a sale are booked by one in-bank conversion, so they land seconds apart, and
  the bank converts at its own buying rate, which sits a few percent under the NBU rate. Each debit
  pairs with one credit, and overlapping candidates pair as many sales as they can. Among
  matchings that pair as many, closer legs are tried first, which is a preference and not a
  guarantee of the smallest total gap. Only the UAH leg is recorded; the debit never is.
- `OwnTransfer` for a credit whose counterparty IBAN is one of the owner's own known bank accounts.
- `Income` for everything else.

A sale outranks an own transfer, since the UAH leg of a sale may name the owner's own account as its
counterparty. A statement holds one account, and each account walks its whole backfill before the
next starts, so the two legs of a sale can be read months apart in either order. The sync therefore
keeps every settled debit of a foreign-currency FOP account (never as a transaction, and not in the
backup, since a restore walks the history again), and each window of any account classifies its new
credits together with the stored UAH legs and stored foreign debits around it. Whichever leg is read
second finds the other, with no extra call to the bank. The range read around a window grows until
nothing outside it is within 60 seconds of what it holds, so every leg that can compete for a debit
is seen together, and two legs never both claim one debit across a window's edge. An unreviewed
stored leg moves to `FxSale` once it pairs, and one once suggested as a sale that no longer pairs,
because the real leg settled later and closer or took its debit, moves back to `Income`, so a stale
guess never keeps income out. It goes back to `Income` even when its counterparty is one of the
owner's own accounts, because a transaction does not store the counterparty's IBAN; the owner sees
it unreviewed and changes the kind. Rates for the debits are read before the owner's lock is taken. A
non-income suggestion
carries a system reason (`monobank: own transfer`, `monobank: currency sale`), which satisfies
Rule 1 until the owner writes their own.

An unreviewed row counts toward income under its suggested kind, exactly as a confirmed one: the
figures never wait for a review to include money that arrived, and the dashboard warns while any
row awaits review. The owner confirms a suggestion in one action or changes the kind through the
transaction edit, where a non-income kind still needs a reason. Confirming or saving any edit makes
the row `Confirmed`. Confirming names the kind the owner saw; if a sync has moved the suggestion
since, the confirmation is refused and the row is shown again with its new kind.

A sync only inserts. An operation is the bank's `id` on its account; once a row holds it, a later
sync leaves that row as the owner last saved it, whatever the bank now says, so an owner's edit and
the fixed rate always win. The one exception is the sale pairing above, which moves an unreviewed
row's suggestion and nothing else: an unreviewed row carries no owner decision to override. A credit
that could not be recorded (on hold, another currency, a failed check, an NBU rate not yet
published) writes nothing and is counted as skipped; the next sync tries it again. A new budget payment
candidate counts as imported. Other debits and operations already recorded count as neither imported
nor skipped, so a repeated sync reads 0 and 0.

Deleting an imported row does not remove it: it becomes `Dismissed`, a tombstone that counts in no
figure (income, periods, the dashboard and its limit, exports) and appears in no list, while still
holding its operation id, so no later sync records the operation again. The backup carries the
tombstone, so a restore followed by a sync does not bring it back either; a file whose dismissed row
names no bank operation or still links a receipt is refused. A row the owner typed is deleted
outright, as before. Dismissing a receipt that pays an invoice (Rule 14) unlinks it from the invoice and clears
its invoice number, so the invoice reopens.

A settled debit on a followed UAH FOP account whose counterparty IBAN is a Treasury account becomes
a budget payment candidate (#80), in the same database transaction as the window's transactions. A
Treasury account is recognised by NBU bank id `899998` at IBAN positions 5 to 10; every Treasury
account carries it, budget and ESV alike, and Є-Казна rejects a payment whose recipient account and
code do not match, so a settled payment's recipient is trustworthy. The budget classification code
is not in the IBAN and regional accounts are re-issued (the military levy's on 1 July 2026), so the
app keeps no national account table. A candidate is one operation id on its account whatever its
status (`Pending`, `Confirmed`, `Dismissed`), so a sync never offers it again once resolved. A
candidate counts in no figure until it is confirmed, and the dashboard's review warning counts
pending candidates together with unreviewed transactions.

Its suggested kind is worked out each time the list is read, first match wins:

1. The kind the owner last confirmed for a candidate to the same IBAN. The owner pays the same few
   accounts every time, so after one confirmation every pending and later payment to that account
   is suggested the same, a backfill's included.
2. The purpose (the bank's description and the payer's comment): `ЄСВ`, `єдин… (соціальн…) внес…`
   or code `71040000` is ESV; `ВЗ`, `військов… збір/збору` or code `11011000`, `11011700`,
   `11011800` is the military levy; `ЄП`, `єдин… подат…` or code `18050400` is the single tax. Case
   does not matter, and the old `*;101;<РНОКПП>;…;;;` form reads the same. A purpose naming more
   than one kind suggests none, and the account hint below does not apply to it either.
3. An IBAN whose account part starts with `00003556` (balance account 3556 of the regional tax
   office) is ESV.
4. Otherwise nothing is suggested and the owner picks the kind.

The period is prefilled with what the home screen would record for that kind: the oldest open
quarter of its debt, or the month of a Rule 6 advance, and the quarter of the payment date when the
kind owes nothing. The owner can change the kind and the period before confirming.

Confirming creates a `BudgetPayment` carrying the operation's `ExternalId` and `BankAccountId`, dated
by the Kyiv date of the operation and for its amount, which moves the balances and the next step
as any payment does. A payment the owner typed with the same date, kind and amount and no bank
operation is offered as the match; accepting it links that payment to the operation, keeping its
period and note, instead of creating a second one. Confirming without a link while such a payment
exists is refused, so a payment typed after the list was read is still offered rather than recorded
twice, unless the owner says to record separately (`recordSeparately`), for a genuinely second
payment of the same date, kind and amount; then a new payment is created and the typed one is left
alone. Confirming a candidate already confirmed or dismissed, or linking a payment that no longer
matches, is refused too. Dismissing hides the candidate for good.

A payment that carries a bank operation keeps its candidate in step, in the same transaction.
Deleting the payment makes the candidate pending again, clearing its confirmed kind and resolved
time: the operation is offered again and the deleted confirmation no longer feeds the kind learned
for its IBAN. Changing the payment's kind changes the candidate's confirmed kind to the new one, as
the owner's latest word on that IBAN. A dismissed candidate is never revived.

Treasury accounts (#98). The app remembers where each kind (single tax, military levy, ESV) is paid,
one account per owner and kind: IBAN, recipient name and recipient code (8 digits, the EDRPOU). Confirming a
candidate as a kind records the operation's counterparty IBAN, name and code as that kind's Learned account,
with the operation and its date. The latest operation wins, whatever order the owner confirms in: an operation
paid before the one the account learned from does not replace it, and of two paid the same day the later
confirmation wins. A name or code the operation lacks (the bank sends a code for company accounts only, and a
private 10-digit code is not a recipient code) is stored as missing, or kept from another confirmed operation
to the same IBAN, older ones included, and settings shows what is missing. A sync that sees an operation again
with a code its candidate lacked stores the code, and the account gains it when the operation is of the Learned
IBAN, whether or not the account learned from that very operation. The
owner can instead enter an account by hand: a Treasury IBAN (bank id `899998` at positions 5 to 10 and a valid
ISO 13616 mod-97 check; a rejected IBAN is told why: its length when it is not 29 characters, a missing `UA`,
a bank id other than `899998`, or a failed check), a name of at most 140 characters and an 8-digit code, all three required. A Manual
account wins over Learned. Confirmations still record the Learned details beside it, and one that becomes the
Learned account with another IBAN than the Manual account raises a notice, shown where the owner confirmed it
and in settings, kept until the owner dismisses it or enters or reverts the account; the Manual account is
never overwritten. A confirmation to the Manual account's own IBAN clears the notice. Reverting drops the
Manual account so the Learned one is in use again, and is refused when nothing was ever learned. A retracted
confirmation stops teaching: when the payment of an operation of the account's Learned IBAN is deleted, or
its kind is changed (the one the account learned from, or an older one that filled in its name or code), the
account learns again from the latest confirmed operation of its kind still standing, or
forgets the Learned account (and its notice) when none is left; a notice stays only while the Learned IBAN
still differs from the Manual one. A changed kind also teaches the new kind by the same rule. A restore brings
the accounts back as they were, including the source and any notice.

---

## Rule 13. Tax reserve

The reserve is a derived figure. It introduces no accrual, deadline or allocation of its own; it reads
what Rules 3, 5 and 7 already produce, so it never changes a balance and no payment reads it back.
Unlike a balance it adds the kinds together (as the tax burden does), because the owner sets aside
one sum.

Per receipt, the amount to set aside is its hryvnia amount times the single-tax and military-levy
rates of the year of its `ValueDate`, each rounded once (Rule 10), from `TaxYearConfig` and never from
code. A non-income kind sets aside zero. A refund sets aside the negative of what its amount would
have at the refund year's rates, which releases reserve. A receipt does not know where it sits against
the limit, so it sets aside at the single tax rate even past it; the excess tax of Rule 4 is in the
total below, which reads the accruals. A row Rule 8 leaves out of income (before registration, or a refund of
such a receipt) shows none. Each receipt rounds on its own, so the receipts of a quarter can differ
from the quarter's accrual by a kopeck; the total below is read from the accrual, not summed from
receipts.

The total needed for taxes is, per kind and per obligation, what has accrued by today minus what the
Rule 7 allocation put against it, floored at zero, added over the ledger:

- A quarter that has ended counts its whole accrual.
- The current quarter counts its single tax and military levy on the income so far, and its ESV for
  the months begun, the current month included (ESV accrues up front, Rule 7, but is not yet owed for
  months to come).
- A later quarter counts nothing, so money paid ahead of it does not lower what is needed now.

The crossing quarter's single tax includes its excess tax, and the quarters after it, in that year
and in every later year until the owner is back on group 3, have no obligations (Rule 4), so they add
nothing; the home screen says the FOP must switch instead, and that ESV and the general system's taxes
are still owed though the app does not compute them.

The figure is grouped by the obligation's due date (Rule 5), oldest first, with each kind kept
apart inside a group. It is the same in `Quarterly` and `MonthlyAdvance` mode, because the mode
changes recommendations only (Rule 6). It needs a registration date and does not need a bank.

### The reserve jar

The owner may choose one monobank jar as the place the reserve is kept. Only a jar in hryvnias is
offered, so its balance is compared with the total without a conversion. The app keeps the jar's id,
name and last balance with the time the bank reported it. It compares the stored balance with the
total needed and shows one of two results; the dashboard never asks the bank.

- **Covered.** The balance is at least the total. The surplus is the balance minus the total, zero
  when they are equal.
- **Short.** The balance is below the total. The dues are walked oldest first and added up; the first
  due date at which the sum exceeds the balance is the date to top up by, and the amount to top up is
  that sum minus the balance, so a balance that pays the early deadlines is short only from a later
  one. The whole gap (the total minus the balance) is shown beside it when it is larger. A date that
  has passed reads as overdue. A negative balance counts as zero.

The balance is refreshed from `client-info` on every sync run of the owner (webhook, nightly and "sync
now") and on the owner's explicit refresh. monobank allows one `client-info` call a minute for a
token, so every read takes the rate gate's `client-info` slot, and an answer under a minute old is
reused instead of asked for again. A read that finds the slot taken is skipped by a sync run and
answered `429` with the seconds to wait for an explicit refresh. A balance that cannot be refreshed
stays as it was and the screen always shows when it was true; one more than 24 hours old is marked
stale. A jar the bank no longer reports (closed, or a different token) is kept the same way until the
owner chooses another or removes it.

The jar's name and balance are the owner's savings: they are shown to the owner only, are not in the
change log, and the token that reads them is never returned. Disconnecting monobank leaves the stored
balance and its time. The choice, the balance and its time are in the backup.

---

## Rule 14. Invoicing

An invoice is the primary document of a service export (Law 959-XII art. 6; payment is acceptance
under Civil Code art. 642(2) when the invoice says so). It carries the requisites of Law 996 art. 9 and,
because primary documents are kept in Ukrainian (MinFin Regulation No. 88), every label and every line
is printed in English and Ukrainian; texts that exist once (a name, an address) are printed as entered.

Numbering. An invoice gets its number when it is issued, never before, so abandoned drafts leave no
gaps. The number is `YYYY-NNN`: the year of the issue date and the largest sequence already issued in
that year plus one, taken under the owner's lock and backed by a unique index, so concurrent issues get
distinct consecutive numbers. A cancelled invoice keeps its number. No statutory scheme exists; this
one is a choice for uniqueness and order.

Completeness. Issuing is refused, with every gap named, until the seller's name in Ukrainian and in
Latin letters, the RNOKPP, the address in both languages, the payment details of the invoice's
currency, the client's legal name, address and country, and both descriptions of every line are
present, and the total is above zero.

Freezing. Issuing copies the parties, the currency's payment details, the clauses and the signature
into the invoice. The issued PDF is rendered from that copy only, so a later change to the invoicing
details or to the client never changes a document already sent, and the lines, dates and currency of
an issued invoice cannot be edited. A draft's preview reads the live data and is marked DRAFT.

Amounts. Integer minor units of the invoice currency. A line is quantity (in thousandths, at most 100 000 units) times
rate, rounded once half away from zero; the total is the sum of the rounded lines.

Payment. Receipts pay invoices. The owner links a receipt (`Income`) to an issued invoice of their own
in the same currency (a client may pay through an intermediary, so the receipt may be of another client: it keeps
its own client, the screens warn that the payer differs from the invoice's client, and a receipt with no client
takes the invoice's), from the invoice or from the receipt. Several receipts can pay one invoice; a
receipt pays at most one. A draft, a cancelled invoice, an invoice already paid, a receipt in another
currency and a receipt already paying another invoice are refused. Linking writes the invoice's number
into the receipt's invoice number, so the receipts list and every export carry it, and marks an
imported receipt reviewed; unlinking clears the number but keeps the client the receipt adopted on linking. A receipt never linked keeps whatever number
the owner typed. While linked, a receipt keeps its kind, currency and number; the owner unlinks it to
change them, and an invoice with receipts linked cannot be cancelled until they are unlinked.

Paid and overdue are derived on every read, never stored. An invoice is paid when its linked receipts,
each less the refunds linked to it, cover the total in the invoice currency; a partial payment leaves
the difference due. It is overdue when issued, not paid, and today in Kyiv is after the due date, so
it turns overdue on the day after the due date. Unlinking, a refund that uncovers the total, and
deleting a linked receipt reopen the invoice. Deleting a linked receipt removes the link with it in
the same save; dismissing an imported one (Rule 12) unlinks it and clears its number, since a
tombstone counts nowhere and must not keep an invoice paid. The home screen counts overdue invoices.

Suggested payment. An imported receipt waiting for review (Rule 12) is offered as the payment of an
open invoice when it is an `Income` row in the invoice's currency, its amount equals what the invoice
still has due, and the invoice number or the client's name appears in the bank's counterparty name,
description or comment, whoever the receipt's client is. The number must stand alone (`2026-003` is not found in `2026-0031`); the name
is compared without case and extra spaces, apostrophes, dashes and composed letters being treated alike, as a
whole word, and a name shorter than three characters is never matched. A number followed or preceded by
`-` or `/` and a digit (`2026-003-1`) is another number. Overdue invoices are offered too; a paid, draft or cancelled one never is. A receipt
that fits several invoices is offered all of them, the closest due date first, then the lowest number.
The offer is only that: nothing links until the owner confirms an invoice, which is the ordinary link
above. Dismissing the offer hides it for that visit and leaves the receipt unlinked; confirming the
receipt without an invoice (Rule 12) ends the offer for good, because only receipts waiting for review
are offered. It is computed on every read, so it follows a new invoice, a payment or a sync.

Retention. An issued or cancelled invoice is kept for at least 1095 days from the day the declaration
covering its income was filed, or from that declaration's deadline if it was not (Tax Code art. 44.3).
The period is extended by the time limitation periods were suspended under martial law. The app never
deletes an issued or cancelled invoice; deleting is only for drafts.

Restore. A restore replaces the owner's data with a file, but never drops an issued or cancelled invoice: a
number that is already out in the world must not be reused by the next issue. A file that lacks the
number of any issued or cancelled invoice the owner holds is refused, naming those numbers, and nothing
is changed. Drafts hold no number and may be dropped.

---

## Rule 15. Declaration

The app fills the group 3 lines of the single tax declaration (form F0103309, MinFin order No. 578 as
amended by No. 57) from what Rule 3 already accrued; it applies no rate of its own, so the declaration
and the periods screen cannot disagree. Nothing is filed from the app: the owner files in the
Electronic Cabinet.

The reporting period is cumulative from 1 January: Q1 is the quarter, Q2 the half-year, Q3 nine
months, Q4 the year. After a return to group 3 (Rule 4) it runs from the quarter of the return, so
that quarter's lines 13 and 24 are zero. The lines, without VAT:

- 06: income through the quarter's end (Rules 1, 2 and 8) up to the year's limit, taxed at 5%.
- 07: income through the quarter's end over the limit, taxed at 15% (Rule 4); zero except in the
  crossing quarter.
- 08: lines 06 and 07, all the income through the quarter's end.
- 09: line 07 at the year's excess rate, rounded once (Rule 10).
- 11: line 06 at the year's single tax rate, rounded once.
- 12: lines 09 and 11, the total tax.
- 13: line 12 of the previous quarter's declaration of the same year; zero in Q1.
- 14.1, and 14 (14.2 is group 4 only): line 12 minus line 13, what the period adds to pay.
- 23: the military levy on lines 05 to 07, which here is line 08 at the year's levy rate.
- 24: line 23 of the previous quarter's declaration; zero in Q1.
- 25: line 23 minus line 24.
- 21: ESV from annex 1, on the year's last group 3 declaration only: the annual (Q4) one, or the
  crossing quarter's when the limit is crossed in Q1 to Q3. It is the sum of the monthly ESV of the
  year's group 3 months (Rule 3). Every other quarter leaves it empty.

Every other line stays empty: they belong to other groups, the 3% rate, or corrections. When refunds
shrink the cumulative income, 14.1 and 25 come out negative and are shown as the arithmetic gives
them; the form has no separate line for that, and Rule 7 settles the negative part as credit. The XML
carries them the same way, with a minus sign: the schema's amount type (`DGdecimal2`) allows one. The
rates are the declared year's `TaxYearConfig` rates, never code. A quarter after the crossing quarter
(Rule 4), in the crossing year or a later one, has no group 3 declaration until the owner is back on
group 3: no figures are shown, the screen says the FOP must file under the system it moved to, the
declaration is not ready, and the home screen does not name it as due.

Crossing in Q1 to Q3 (settled in #112, ADR-018): the year's ESV for the group 3 months goes on the
crossing quarter's declaration, the last group 3 declaration of the year, with annex 1 marked "перехід
на сплату інших податків і зборів" (H03). The annex's own footnote 9 describes exactly this case, a FOP
who moved to other taxes, with item 8 giving the stretch spent on the simplified system; without it the
group 3 months' ESV would be declared nowhere. A Q4 crossing marks the annual annex the same way, since
the switch follows it.

Worked example, the crossing quarter: a limit of 10,091,049.00 UAH and receipts of 4,000,000.00 in
February, 4,000,000.00 in May, 1,500,000.00 in August and 1,000,000.00 in September cross it in Q3:

| Line | Q3 |
| --- | --- |
| 06 | 10,091,049.00 |
| 07 | 408,951.00 |
| 08 | 10,500,000.00 |
| 09 | 61,342.65 |
| 11 | 504,552.45 |
| 12 | 565,895.10 |
| 13 | 400,000.00 |
| 14.1 / 14 | 165,895.10 |
| 23 | 105,000.00 |

A declaration file, and annex 1 with it, is built only once the quarter's last day has passed in
Kyiv: from the next day, and from 1 January for Q4 (#163). Before that the screen shows the figures as a
preview, the download is disabled with the date it opens, and the api answers 409 to a request for the
file. A file generated before that day, by Kyiv date, is stale: its figures were incomplete, and
filing it would be wrong. It is never listed or served, even after the quarter ends: its download answers
409 (`GeneratedBeforeQuarterEnded`) and the owner generates the file again. The stored row stays, so a
backup and its restore are unchanged.

A quarter that ends before the registration date (Rule 8) has no declaration. In a first year
registered in May, Q1 has none, Q2's line 13 is zero, and income before registration is not in line
06.

Worked example, 2026 (5% and 1%), registered before 2026, receipts of 123,456.78 UAH on 20 January
and 98,765.43 UAH on 15 April:

| Line | Q1 | Q2 |
| --- | --- | --- |
| 06 / 08 | 123,456.78 | 222,222.21 |
| 11 / 12 | 6,172.84 | 11,111.11 |
| 13 | 0.00 | 6,172.84 |
| 14.1 / 14 | 6,172.84 | 4,938.27 |
| 23 | 1,234.57 | 2,222.22 |
| 24 | 0.00 | 1,234.57 |
| 25 | 1,234.57 | 987.65 |

The declaration is ready when nothing below blocks it. These block:

- an imported receipt of the year dated up to the quarter's end still waiting for review (Rule 12);
- a budget payment candidate still pending whose payment date in Kyiv falls in the quarter (a
  candidate of another quarter does not block this one);
- the year's `TaxYearConfig` not verified (Rule 9);
- no registration date (Rule 8);
- a missing detail: the name and RNOKPP (read from the invoicing details, never stored twice), the
  tax office (its region and district codes and its name), at least one KVED code (the first is the main
  activity), and the address as in the register;
- a quarter outside group 3 after a crossing, as above. The crossing quarter itself does not block,
  since its lines 07 and 09 are filled.

What the Rule 7 ledger still owes per kind, across years, from every obligation that has fallen due
by the quarter's filing deadline (Rule 5), is shown as a warning and never blocks: paying is not
filing. Debt left over from an earlier year counts; the quarter's own tax does not, since it falls due
after the filing deadline.

The owner marks a quarter's declaration as filed, per year and quarter: the date it was filed (after
the quarter's end and not after today in Kyiv) and its type, reporting, new reporting or clarifying.
Marking does not require readiness, marking again replaces the mark, and the mark can be undone. The
mark keeps line 08 as it stood; when a later change to the year's receipts moves line 08 away from
it, the mark is flagged as changed since filing, a hint that a clarifying declaration may be needed,
until the owner marks the quarter again. The home screen names the last ended quarter's declaration
from the day after the quarter ends through its due date (Rule 5), until it is marked filed.

The declaration file (#111). For a ready quarter with figures the owner downloads the declaration as an
F0103309 XML file of the chosen type (reporting by default), imports it in the Cabinet ("Імпортувати XML
з пристрою"), checks it, signs it with a KEP and sends it there; the app never signs or sends (ADR-016).
A quarter that is not ready, or is outside group 3, gets no file. The file follows the DPS format:

- windows-1251, the lowercase declaration `<?xml version="1.0" encoding="windows-1251"?>`, no whitespace
  between elements, elements in the schema's order;
- the header: TIN and HTIN the RNOKPP, C_DOC F01, C_DOC_SUB 033, C_DOC_VER 9, C_DOC_TYPE 0, C_DOC_CNT 1,
  C_REG and C_RAJ the tax office codes, C_STI_ORIG = C_REG × 100 + C_RAJ, PERIOD_TYPE 2, 3, 4 or 5 with
  PERIOD_MONTH 3, 6, 9 or 12 for Q1 to Q4, C_DOC_STAN 1, 2 or 3 for reporting, new reporting or
  clarifying, D_FILL and HFILL the day it is prepared in Kyiv as `ddmmyyyy`;
- the body: the type flag (HZ, HZN or HZU), the period flag (H1KV, HHY, H3KV or HY) and year, for a
  clarifying declaration the same quarter as the period clarified, HSTI the tax office's name, HNAME and
  HBOS the invoicing details' Ukrainian name without a leading "ФОП", HLOC the address, HNACTL 0, the
  KVED codes in table 1 with empty names, and the lines above with two decimals. Lines 07 and 09 are
  written only when nonzero; line 21 only with annex 1, below; every other line is left out;
- the name, per standard No. 729: C_REG and C_RAJ (two digits each), the TIN padded to 10, F01, 033,
  C_DOC_VER as two digits, C_DOC_STAN, C_DOC_TYPE as two digits, C_DOC_CNT as seven, PERIOD_TYPE,
  PERIOD_MONTH as two digits, PERIOD_YEAR, C_STI_ORIG as four, `.xml`. The Q1 2026 reporting
  declaration of RNOKPP 1234567890 at office 26/50 is `26501234567890F0103309100000000120320262650.xml`.

C_DOC_CNT stays 1: a second filing of the same type in a period may need a higher one, and the app does
not handle that yet.

Annex 1, the ESV annex (#112). The year's last group 3 declaration (above) comes with a second file,
form F0133109 (C_DOC F01, C_DOC_SUB 331, C_DOC_VER 9), "Відомості про суми нарахованого доходу
застрахованих осіб та суми нарахованого єдиного внеску". An ESV exemption (Rule 3) or a year without a
registration date has no annex and no line 21: the form's first footnote says exempt payers do not file
it. The two files are prepared, validated, stored and downloaded together:

- the annex's header repeats the declaration's (TIN, tax office, period, C_DOC_STAN, C_DOC_TYPE 0,
  C_DOC_CNT 1, D_FILL) with its own C_DOC_SUB, and its name follows standard No. 729 the same way, with
  F0133109 in place of F0103309;
- each file links the other in LINKED_DOCS, one DOC with NUM 1: the declaration names the annex with
  TYPE 1 (an annex), the annex names the declaration with TYPE 2 (the main form). The declaration sets
  HD1, "annex 1 attached", and line 21 (R021G3) to the annex's total;
- the annex's body: the type flag, HTIN, HNAME, the period flag and year (for a clarifying one the same
  period clarified), H03 on a crossing quarter's, HKVED the first KVED code, R08G1D to R08G2D the stretch
  on the simplified system within the year (from 1 January, the registration date or the first day of
  the quarter of a return to group 3, whichever is latest, to the last day of the declaration's
  quarter), R081G1 6 (a FOP on the simplified system, footnote 11), then per group 3 month that owes ESV
  its base (R09nG2, the minimum wage, or under `Prorated` its prorated part in the registration month), the year's
  `EsvRateBp` as a percentage (R09nG3) and the ESV (R09nG4), months without ESV left out, the totals
  R09G2 and R09G4, and HBOS;
- a clarifying annex leaves item 10 (the correction of an earlier annex's ESV) empty: the app keeps no
  figure of the annex that was filed, so the owner fills it in the Cabinet.

Annex 2 (F0133209, the minimum tax liability) is not produced: it is filed by owners and users of
agricultural land, which this FOP is not, so its HD2 flag stays unset.

Every file is validated against the vendored schemas before it is kept or downloaded. A text the
encoding cannot carry, or a control character XML 1.0 forbids (the modifier apostrophe U+02BC becomes `'` first) or a value the schema refuses,
such as a tax office code whose C_STI_ORIG is not a DPS office, stops the download with the list of
errors and keeps nothing. The last file prepared per quarter and type is kept with the time it was
prepared, as a record of what was filed, and is in the backup.

---

## Rule 16. Paying an obligation

The app prepares a transfer and never makes one: it holds no bank write access, and the owner confirms
the payment in the bank. Ready details are a copyable set of fields, and a QR (#100) carries the same
details.

The Pay panel is offered on every debt the home screen shows, which is the oldest open quarter of a
kind, or the advance's month, as a recorded payment names it (Rule 7), and per kind and quarter on the
payments screen. Only periods the app computes are offered: a year with a `TaxYearConfig` and a quarter
in group 3 (Rule 4); a month counts as its quarter. Any other period is refused.

The recipient is the kind's Treasury account in use, Manual over Learned (Rule 12). When the IBAN, the
recipient name or the recipient code is missing, the panel lists what is missing and gives no recipient
details, never a partial set. The amount starts from what is owed for that kind and period; the owner may
change it, and it must be a positive whole number of kopecks. One transfer covers one kind.

The purpose follows MinFin Order 148: the payment type code `101`, the kind in words (`єдиний податок`,
`військовий збір`, `єдиний внесок`), ` за `, the period and ` року`. A quarter is a Roman numeral, a month
is its lowercase nominative name, and the year is the period's year:

- `101 єдиний податок за III квартал 2026 року`
- `101 єдиний внесок за вересень 2026 року`

The RNOKPP is not in the purpose, it travels as the payer code of the transfer. The old `*;101;...`
format is never produced. This phrasing (nominative month after `за`, year with `року`) is the decision;
the order fixes the elements, not the grammatical case of the month.

The QR (#100) follows NBU Resolution No. 97 of 19.08.2025 as amended by No. 128, format 003
(Appendix 4, verified against the text on zakon.rada.gov.ua): `https://qr.bank.gov.ua/` and the Base64URL
(no padding) of seventeen LF-separated UTF-8 fields: `BCD`, `003`, `1` (UTF-8), `UCT`, an empty reserved
field, the recipient name, the IBAN, the amount, the recipient code, the category/purpose code, an empty
reference, the purpose, an empty display text, the field-lock mask `FEFF` (only the amount editable; the
resolution requires fields 1-5, 11 and 14-17 locked), and empty validity, creation time and signature. The
amount is `UAH` and the shortest form: no fraction for whole hryvnias (`UAH3`), otherwise exactly two
digits (`UAH1234.50`, never `UAH1234.5`), at most `UAH999999999.99`. The whole content is at most 507
bytes. A recipient or amount that does not fit gives no QR, and the copy buttons still serve.

The image uses error correction Q, or M when Q does not fit, a QR version from 10 to 17, and the hryvnia
sign in a white circle at the centre whose diameter follows the version (17 modules for version 10, 19
for 11-12, 21 for 13, 23 for 14-15, 25 for 16-17), the sign inscribed in a circle 4 modules smaller.

Unverified until the owner scans a Treasury payment with a real banking app: the category/purpose code
(`TAXS/TAXS`, the ISO 20022 "Tax Payment" code in both lists; the resolution's only example is
`SUPP/SUPP`, and no NBU or bank text names one for budget transfers), whether a bank accepts a QR for a
budget account at all, and whether it splits the leading `101` into the payment type field. If banks
refuse, the QR is hidden for Treasury accounts behind one flag in the panel, with a note.

Once the bank operation is confirmed (#80), the debt moves as it does for any payment.

---

## Rule 17. Reminders

The app reminds the owner of each date that still has something to do, through every channel the owner
connected and switched on (Telegram and email). Nothing is scheduled ahead: every run
computes the reminders due from the Rule 7 ledger, the filed marks and the settings, so the amount is what
is owed when the message is sent, and a payment recorded in between changes the amount or drops the
reminder.

What is reminded:

- A payment deadline (Rule 5) of a kind whose obligation there still owes something after Rule 7's
  allocation, with that remainder. A kind paid in full is left out, and a date with nothing owed has no
  payment reminder.
- In `MonthlyAdvance` mode, each advance's recommended date (Rule 6) while the advance through that month
  has anything left, with the unpaid remainder of the quarter's earlier months added, as on the home
  screen. An advance dated on or after its quarter's own deadline is left to the quarterly reminder.
- A quarter's declaration deadline (Rule 5) while the quarter is not marked filed (Rule 15), for every
  group 3 quarter that ends on or after the registration date.

A quarter outside group 3 after a limit crossing (Rule 4) has no obligations and no declaration, so
nothing of it is reminded. Everything due on one date comes in one message, and the single tax and the
military levy keep an amount each.

The moments are 7 days before, 1 day before and on the date, each at 09:00 in Kyiv, and, for a payment
still owed, the day after at 09:00. The date is the shifted one the app shows (Rule 5). Only the latest
moment that has passed is due, so a server that was down through the 7-day moment and is back after the
1-day one sends one message, which counts the days actually left. A reminder before the date that was
missed is sent late, up to the end of the date. The day-after reminder goes out on that day only, so a
debt is flagged overdue once and a first run never replays old ones. An advance is never overdue (Rule
6), and a declaration is not reminded after its deadline.

Each message is sent at most once per channel. It is recorded per owner, date, kinds, moment and channel
before it is sent, so neither a restart, a redeploy nor two runs at once can send it twice. A date and
moment already sent for every kind a run would name is not sent again, so paying the levy after the 7-day
message does not repeat it; a kind no message of that date and moment named yet, such as a newly owed
one, is sent with everything due. A crash between the record and the send loses that one message rather
than risk a second. A restore does not touch the record, so it never resends. A failure that proves
the message was not delivered (Telegram unreachable, rate limiting or a server error, after the channel's
own retries) removes the record, so a later run tries again within the window. A timeout may have
delivered the message before the answer was lost, so it is neither retried nor released: the record is
kept as possibly sent. Any other failure keeps the record too, and shows on the channel in settings; a blocked bot also switches the channel off.

A message is plain text in the owner's interface language (uk or ru): the date and the days left, one
line per item (the kind and period with the amount owed now, or the declaration to file), and a link to
the app's home screen, where the pay panel is. The runs are 5 minutes apart.

Email is a channel like Telegram, through the same sender, the same sent log (the channel is part of the
claim's key, so one reminder is claimed once for Telegram and once for email) and the same retry rule: three
retries after the first attempt, then a failure shown on the channel. A test message and a confirmation email
are answers to a button the owner pressed, so each makes one attempt and shows its failure on the channel. An address is added unconfirmed and
receives nothing but the confirmation email until the owner opens its link, signed in, within 24 hours; the
link names the address, so a link for an address since replaced or removed opens nothing. Asking for a
confirmation again, changing the address, or restoring a backup (which brings an email address back
unconfirmed) switches the channel off until the new link is opened. The
message is the Telegram text as the plain-text part plus a simple HTML part of the same words. The server's
answers decide the retry: unreachable and 4xx replies are retried and release the claim; a refused
sign-in, a 5xx reply to the sender or the recipient is final and keeps it; a connection lost or timed out
after the message was handed to the server may have delivered it, so it is kept as possibly sent, never
retried (the rule above). Without valid SMTP settings, or without an address for the link to point at, the
channel is unavailable and a run sends nothing and claims nothing.
