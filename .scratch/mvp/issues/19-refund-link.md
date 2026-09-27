# 19: Link a refund to the receipt it reverses

GitHub: #41
Status: closed, done (PR #43)
Blocked by: #5

## What to build

A `RefundToClient` can name the receipt it reverses. A refund of a receipt that Rule 8 excluded (dated before `FopRegistrationDate`) is then excluded too, so it no longer lowers period income or manufactures a tax credit.

The owner chose option 1 from the modelling comment on #5 on 2026-09-27: a nullable self-reference `RefundsTransactionId` on `Transaction`.

## Why

A 100,000.00 receipt on 2026-02-10 (before registration 2026-03-01) refunded on 2026-04-15 currently yields a Q2 single tax of -5,000.00 and a military levy of -1,000.00. That phantom credit is carried forward by Rule 7 against real liability in #9.

## Acceptance criteria

- [x] Engine: `RefundToClient` carries the reversed receipt's value date (or equivalent) and `IncomeLedger` excludes a refund whose original is excluded by Rule 8. A refund with no link behaves exactly as today. Tests cover the case above (Q2 tax 0, no negative cumulative), a linked refund of a post-registration receipt (unchanged), and an unlinked refund (unchanged).
- [x] `knowledge/business-rules.md` states the transitive exclusion under Rules 1 and 8.
- [x] API: `RefundsTransactionId` is optional, only allowed on `RefundToClient`, must point at the same owner's `Income` row, and the sum of refunds linked to one receipt cannot exceed that receipt's amount (in the receipt's own currency). Violations return 400 with a field error. Deleting a receipt with linked refunds is rejected, not cascaded silently.
- [x] Web: the refund form lets the owner pick the receipt it reverses (filtered to that client when one is chosen), and each row shows what a linked refund reverses. A refund of an excluded receipt shows that it does not count.
- [x] Verified live at 375px and 1280px with no horizontal scroll.

## Blocked by

- #5

