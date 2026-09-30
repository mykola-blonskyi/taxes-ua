# 01: Remember the Treasury account for each tax kind

GitHub: #98
Status: ready-for-agent
Blocked by: #80
Parent: #97

## What to build

The app remembers the Treasury account for each kind (single tax, military levy, ESV): IBAN, recipient name and recipient code. Confirming a bank operation as a budget payment (#80) records its recipient as the Learned account for that kind. The owner can enter or correct an account in settings; a Manual account wins over later learned ones, and a confirmation to a different IBAN than a Manual account shows a notice instead of overwriting. Settings shows each account's source (learned from which operation and when, or entered).

## Acceptance criteria

- [ ] A confirmed candidate upserts the Learned account for its kind with IBAN, recipient name and code from the operation.
- [ ] Manual entry validates a Treasury IBAN (bank id 899998, mod-97) and a recipient code of 8 digits; Manual wins over Learned.
- [ ] A confirmation to another IBAN than a Manual account raises a visible notice and leaves the Manual account.
- [ ] Backup round trip; older backups restore; owner isolation.
- [ ] Rule and domain model docs updated.
- [ ] Settings section proved in a real browser at 375 px in uk and ru.
