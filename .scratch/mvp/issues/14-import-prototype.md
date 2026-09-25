# 14: Prototype JSON import

GitHub: #15
Status: ready-for-agent
Blocked by: #12

## Parent

#1

## What to build

The owner uploads the JSON from the current prototype (settings, incomes, mpaid, done), and the app gets receipts with their rates and monthly payments as budget payments. A repeat import creates no duplicates.

## Acceptance criteria

- [ ] Every `incomes` record becomes a transaction with `RateSource: Manual` and a kopeck amount equal to the prototype's `uah` value.
- [ ] `mpaid` records become three payments (EP, VZ, ESV) with a "month" period.
- [ ] Re-importing the same file doesn't change the record count.
- [ ] API test for idempotence.

## Blocked by

- #12 (Monthly advances)
