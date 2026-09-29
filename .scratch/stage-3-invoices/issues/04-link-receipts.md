# 04: Mark invoices paid by linking receipts

GitHub: #93
Status: ready-for-agent
Blocked by: #92
Parent: #89

## What to build

Receipts pay invoices. The owner links one or more receipts in the invoice's currency to an issued invoice, from the invoice or from the receipt (choosing among the client's open invoices). Paid and overdue are derived: paid when linked receipts cover the total, overdue when issued, unpaid and past the due date in Kyiv; the list shows status and amount still due. Linking fills the receipt's invoice number; unlinking, or a full refund of a linked receipt, reopens the invoice. The home screen counts overdue invoices.

## Acceptance criteria

- [ ] Partial payments leave an amount due; covering the total makes the invoice paid.
- [ ] Currency mismatch, a draft or cancelled invoice, and another owner's invoice are rejected.
- [ ] Unlink and full refund reopen the invoice; a receipt with a link cannot be deleted without unlinking (or the link is removed with it; decide and document).
- [ ] Overdue flips on the day after the due date in Kyiv (fake clock test).
- [ ] Exports carry the linked invoice number.
- [ ] Backup round trip of links; owner isolation.
- [ ] Flows proved in a real browser at 375 px in uk and ru.
