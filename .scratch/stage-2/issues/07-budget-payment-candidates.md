# 07: Turn Treasury payments into budget payment candidates

GitHub: #80
Status: ready-for-agent
Blocked by: #78
Parent: #71

## What to build

An outgoing operation from a FOP account to a Treasury account (recognised by the bank code in the counterparty IBAN) becomes a budget payment candidate on the review screen. Its kind (single tax, military levy, ESV) is suggested from the payment purpose; with no confident match it is left for the owner. Its period is prefilled with the oldest open obligation of that kind, as the home screen does (Rule 7). Confirming creates a `BudgetPayment` carrying the operation's `ExternalId` and account, so the same operation never becomes a second payment. A manual payment with the same date, kind and amount and no `ExternalId` is offered as the match; accepting links it instead. Dismissing hides the candidate for good.

## Acceptance criteria

- [ ] Table-driven tests for Treasury recognition and kind suggestion; the bank code and keywords checked against a real tax payment in the owner's statement first.
- [ ] Confirm creates a payment that moves the balances and the next step.
- [ ] Matching links the manual payment; no duplicate appears.
- [ ] Re-syncing never recreates a confirmed or dismissed candidate.
- [ ] `knowledge/domain-model.md` records the candidate and the new `BudgetPayment` fields.
- [ ] Candidate flow proved in a real browser.
