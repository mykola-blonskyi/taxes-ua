# 05: Suggest invoice payments for imported receipts

GitHub: #94
Status: ready-for-agent
Blocked by: #93, #80
Parent: #89

## What to build

On the review screen, an imported monobank receipt that matches an open invoice is offered as that invoice's payment: same currency, amount equal to the invoice's outstanding amount, and the invoice number or the client's name found in the counterparty name, description or comment. Confirming links it (as in the previous ticket); it never links by itself. A receipt with several candidate invoices offers them all, closest due date first.

## Acceptance criteria

- [ ] Table-driven tests for the matcher: number in the comment, client name in the counterparty, amount mismatch, currency mismatch, two candidates.
- [ ] The suggestion appears after a sync (FakeBank harness) and confirming links and marks the invoice paid.
- [ ] Nothing links without confirmation; dismissing the suggestion keeps the receipt unlinked.
- [ ] Proved in a real browser at 375 px in uk and ru.
