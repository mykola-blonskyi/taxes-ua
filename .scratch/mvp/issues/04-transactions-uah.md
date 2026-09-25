# 04: Receipts in hryvnia

GitHub: #5
Status: ready-for-agent
Blocked by: #4

## Parent

#1

## What to build

The owner enters a UAH receipt with a credit date, amount, operation type, client, invoice number and comment, sees the year's list with a total, and edits or deletes with a confirmation. A "not income" type requires a reason. An operation dated before the FOP registration date is flagged with a warning. Amounts are stored in kopecks.

## Acceptance criteria

- [ ] Create, edit and delete work from the screen and via the API; invalid requests (amount ≤ 0, non-income without a reason) return 400 with details.
- [ ] The list filters by year; the hryvnia total equals income rows minus refunds to clients.
- [ ] An operation with a `ValueDate` earlier than `FopRegistrationDate` shows a warning in the list.
- [ ] API tests for validation and for excluding pre-registration operations from the total.

## Blocked by

- #4 (Year parameters and FOP settings)
