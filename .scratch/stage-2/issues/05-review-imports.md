# 05: Review imported transactions with suggested kinds

GitHub: #78
Status: ready-for-agent
Blocked by: #76
Parent: #71

## What to build

A review screen lists every transaction with `NeedsReview`, newest first, with its suggested kind. Suggestions come from a pure classifier: a credit whose counterparty IBAN is one of the owner's own accounts is `OwnTransfer`; a UAH credit that pairs with a same-moment foreign-currency debit on the owner's own FOP account is `FxSale`; everything else is `Income`. The owner confirms in one action or changes the kind, giving a reason for non-income as Rule 1 requires. The dashboard warns while anything awaits review and links to the screen. Deleting an imported transaction marks it `Dismissed` (excluded from every figure) so the next sync does not recreate it; manual rows delete as today.

## Acceptance criteria

- [ ] Table-driven unit tests for the classifier cover own transfer, currency sale and income.
- [ ] Confirming sets `Confirmed`; reclassifying to non-income without a reason is rejected.
- [ ] The dashboard warning appears with pending rows and disappears when none remain.
- [ ] A deleted imported row stays gone after sync and counts nowhere.
- [ ] Review screen and dashboard warning proved in a real browser, including 375px width.
