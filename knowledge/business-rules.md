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
  pairs with one credit, and overlapping candidates pair as many sales as they can, the closest
  first among equals. Only the UAH leg is recorded; the debit never is.
- `OwnTransfer` for a credit whose counterparty IBAN is one of the owner's own known bank accounts.
- `Income` for everything else.

A sale outranks an own transfer, since the UAH leg of a sale may name the owner's own account as its
counterparty. A statement holds one account, and each account walks its whole backfill before the
next starts, so the two legs of a sale can be read months apart in either order. The sync therefore
keeps every settled debit of a foreign-currency FOP account (never as a transaction, and not in the
backup, since a restore walks the history again), and each window of any account classifies its new
credits together with the stored UAH legs and stored foreign debits around it. Whichever leg is read
second finds the other, with no extra call to the bank. An unreviewed leg in the window moves to
`FxSale` once it pairs, and one once suggested as a sale that no longer pairs, because the real leg
settled later and closer, moves back to `Income`, so a stale guess never keeps income out. A
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
outright, as before.

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

---

## Rule 13. Tax reserve

The reserve is a derived figure. It introduces no accrual, deadline or allocation of its own; it reads
what Rules 3, 5 and 7 already produce, so it never changes a balance and no payment reads it back.
Unlike a balance it adds the kinds together (as the tax burden does), because the owner sets aside
one sum.

Per receipt, the amount to set aside is its hryvnia amount times the single-tax and military-levy
rates of the year of its `ValueDate`, each rounded once (Rule 10), from `TaxYearConfig` and never from
code. A non-income kind sets aside zero. A refund sets aside the negative of what its amount would
have, which releases reserve. A row Rule 8 leaves out of income (before registration, or a refund of
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

The figure is grouped by the obligation's due date (Rule 5), oldest first, with each kind kept
apart inside a group. It is the same in `Quarterly` and `MonthlyAdvance` mode, because the mode
changes recommendations only (Rule 6). It needs a registration date and does not need a bank.

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

Retention. An issued or cancelled invoice is kept for at least 1095 days from the day the declaration
covering its income was filed, or from that declaration's deadline if it was not (Tax Code art. 44.3).
The period is extended by the time limitation periods were suspended under martial law. The app never
deletes an issued or cancelled invoice; deleting is only for drafts.

Restore. A restore replaces the owner's data with a file, but never drops an issued or cancelled invoice: a
number that is already out in the world must not be reused by the next issue. A file that lacks the
number of any issued or cancelled invoice the owner holds is refused, naming those numbers, and nothing
is changed. Drafts hold no number and may be dropped.
