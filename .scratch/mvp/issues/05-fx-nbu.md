# 05: Currency receipts and the NBU rate

GitHub: #6
Status: closed, done (PR #42)
Blocked by: #5

## Parent

#1

## What to build

The owner enters a USD or EUR receipt; the NBU rate is filled in automatically for the credit date, and the hryvnia equivalent is visible before saving. For a weekend date, the last business day's rate is used, with its date shown alongside. The rate can be corrected manually, then the source is marked manual. When NBU is unavailable, the form shows a clear message and still allows a manual rate. The rate and the kopeck amount are fixed at save time.

## Acceptance criteria

- [x] A rate request for a Saturday returns Friday's rate with `RateDate` set to Friday; a repeat request is served from the `FxRate` cache, not from NBU.
- [x] The kopeck amount equals `roundHalfUp(AmountMinor × RateE4 / 10⁴)` and never changes after saving even if the cache changes.
- [x] NBU unavailability returns an external-dependency error, not a 500, and the form keeps working.
- [x] API test with a substituted HTTP handler for NBU: an empty Saturday response, a network error, a normal response.

New criteria, from independent verification of the engine:

- [x] The decimal-to-`RateE4` conversion lives in `Features/Fx/`, not in `TaxesUa.Engine`.
- [x] `Money.ToRateE4` and its test are deleted, and `grep -rn 'decimal\|double\|float' api/src/TaxesUa.Engine/` returns nothing.
- [x] A test covers a rate the NBU returns with more than four decimal places. `44.9729m` must still yield `449729`.

## Blocked by

- #5 (Receipts in hryvnia)
