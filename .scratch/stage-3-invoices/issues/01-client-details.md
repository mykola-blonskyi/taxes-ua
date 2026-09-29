# 01: Keep client details for invoices

GitHub: #90
Status: ready-for-agent
Blocked by: none
Parent: #89

## What to build

A Clients screen where the owner adds and edits each client's legal address, country, tax or VAT id, email, default currency and notes. Clients created by receipts (name only) gain details in place; renaming keeps receipts linked; a client with receipts (and later invoices) cannot be deleted. Client details are in the backup.

## Acceptance criteria

- [ ] Existing name-only clients appear and can be completed; names stay unique per owner.
- [ ] Renaming keeps every receipt linked; deleting a client with receipts is rejected.
- [ ] Backup and restore round-trip the new fields; older backups still restore.
- [ ] Owner isolation tested at the HTTP seam.
- [ ] knowledge/domain-model.md updated.
- [ ] Screen proved in a real browser at 375 px in uk and ru.
