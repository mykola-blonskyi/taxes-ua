# 08: Budget payments and balances

GitHub: #9
Status: ready-for-agent (engine half merged in PR #30; payments endpoint and web screens remain)
Blocked by: #8

## Parent

#1

## What to build

The owner records an actual payment with a date, kind (EP, VZ, ESV), amount and period, and sees, per kind, accrued, paid and the remainder or overpayment separately. An overpayment carries forward to the next period of the same kind and never offsets a different kind's debt. The quarters table gains "paid" and "remaining" columns.

## Acceptance criteria

- [ ] CRUD for payments from the screen and via the API, with period validation (quarter 1–4 or month 1–12).
- [ ] Engine tests: an ESV overpayment covers the next quarter; an EP overpayment never reduces an ESV debt; obligation statuses `Upcoming`, `Due`, `Overdue`, `Done`.
- [ ] The balances panel shows an overpayment with its sign and explains the carry-forward.
- [ ] API test: the obligations response with paid and remaining matches the engine.

## Blocked by

- #8 (Accruals and the declaration numbers)
