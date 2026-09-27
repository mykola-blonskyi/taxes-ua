# 08: Budget payments and balances

GitHub: #9
Status: closed, done (engine half PR #30; payments ledger, API and screens PR #46). FIFO
allocation and cross-year carry, added by #47 (PR #55), now live inside `Balances.ForYears`.
Blocked by: #8

## Parent

#1

## What to build

The owner records an actual payment with a date, kind (EP, VZ, ESV), amount and period, and sees, per kind, accrued, paid and the remainder or overpayment separately. An overpayment carries forward to the next period of the same kind and never offsets a different kind's debt. The quarters table gains "paid" and "remaining" columns.

## Acceptance criteria

- [x] CRUD for payments from the screen and via the API, with period validation (quarter 1–4 or month 1–12).
- [x] Engine tests: an ESV overpayment covers the next quarter; an EP overpayment never reduces an ESV debt; obligation statuses `Upcoming`, `Due`, `Overdue`, `Done`.
- [x] The balances panel shows an overpayment with its sign and explains the carry-forward.
- [x] API test: the obligations response with paid and remaining matches the engine.

New criteria, from independent verification of the engine:

- [x] `ObligationBuilder` (now folded into `Balances`) does not depend on the payment deadline falling after the declaration deadline.
- [x] Balances are derived per payment kind from the per-kind fields, never from a pooled total.
- [x] The missing `CumulativeEsvKop` is handled deliberately (ESV has only a per-quarter figure).
- [x] Obligations behave sanely when `EngineWarning.NegativeCumulativeTax` is present.

## Blocked by

- #8 (Accruals and the declaration numbers)
