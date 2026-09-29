# 02: Enter my invoicing details and payment details per currency

GitHub: #91
Status: ready-for-agent
Blocked by: none
Parent: #89

## What to build

An Invoicing section in settings: seller name in Ukrainian and Latin, RNOKPP, address in both languages; payment details per currency (IBAN, beneficiary bank, SWIFT, optional intermediary bank name, SWIFT and account as free text copied from the bank app); bilingual clause texts with defaults (acceptance by payment, fees borne by the payer, single-tax payer not registered for VAT); an optional signature image (PNG or JPEG, size-capped, owner-only). All of it is in the backup.

## Acceptance criteria

- [ ] Details save and reload; IBAN is validated as a Ukrainian IBAN; SWIFT is 8 or 11 characters.
- [ ] Signature upload rejects other types and oversize files; it is served only to its owner.
- [ ] Defaults for the clauses appear until the owner edits them.
- [ ] Backup and restore round-trip everything, including the image; older backups still restore.
- [ ] Owner isolation tested.
- [ ] Section proved in a real browser at 375 px in uk and ru.
