# 03: Draft, issue and download a bilingual PDF invoice

GitHub: #92
Status: ready-for-agent
Blocked by: #90, #91
Parent: #89

## What to build

The owner creates a draft invoice for a client (issue date, due date, currency, lines with English and Ukrainian descriptions, a unit from a fixed bilingual list, quantity and rate), previews its PDF with a DRAFT mark, duplicates a previous invoice, deletes drafts, and issues it. Issuing checks that the seller, buyer and currency payment details are complete, assigns the next number of the year (YYYY-NNN) under the owner lock, and freezes a snapshot of parties, lines, payment details and clauses. The issued PDF is A4, bilingual EN/UK, rendered from the snapshot with the existing MigraDoc setup, and its payment narrative includes the invoice number. An issued invoice can be cancelled with a reason but not edited or deleted. Invoices are in the backup and the change log.

## Acceptance criteria

- [ ] Numbers are sequential per year, unique, and never consumed by drafts; concurrent issues get distinct numbers.
- [ ] Missing requisites block issuing with field errors naming them.
- [ ] Editing details or the client after issuing does not change the issued PDF.
- [ ] The PDF, read back with PdfPig, contains both languages, both parties, the lines, the total with currency, the currency's payment details, the clauses, the number in the payment narrative, and the signature or the seller name.
- [ ] Line amounts and totals are exact in minor units.
- [ ] Cancel keeps the number; issued invoices reject edit and delete.
- [ ] Backup round trip; change-log entries; owner isolation.
- [ ] ADR for the frozen snapshot and bilingual PDF; invoicing rule added to knowledge/business-rules.md (numbering, freezing, bilingual, retention of at least 1095 days after the covering declaration, extended by martial-law suspension).
- [ ] Editor, list and PDF proved in a real browser at 375 px in uk and ru; the PDF opened and inspected.
