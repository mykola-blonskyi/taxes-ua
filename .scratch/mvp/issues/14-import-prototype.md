# 14: Prototype JSON import

GitHub: #15
Status: ready-for-human (merged in PR #61; open for the owner's check against a real prototype export)
Blocked by: #12

## Parent

#1

## What to build

The owner uploads the JSON from the current prototype (settings, incomes, mpaid, done), and the app gets receipts with their rates and monthly payments as budget payments. A repeat import creates no duplicates.

## Acceptance criteria

- [x] Every `incomes` record becomes a transaction with `RateSource: Manual` and a kopeck amount equal to the prototype's `uah` value.
- [x] `mpaid` records become three payments (EP, VZ, ESV) with a "month" period.
- [x] Re-importing the same file doesn't change the record count.
- [x] API test for idempotence.

## Blocked by

- #12 (Monthly advances)
