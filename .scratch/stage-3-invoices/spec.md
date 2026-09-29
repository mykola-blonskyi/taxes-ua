GitHub: #89
Status: ready-for-agent

# Spec: Bilingual PDF invoices linked to receipts

## Problem Statement

The owner bills foreign clients in USD and EUR, and today prepares every invoice by hand in a
separate tool: their own and the client's details, a number that must not repeat, the bank details
for the right currency, and the text that makes a paid invoice stand in for an act of acceptance.
Nothing links an invoice to the receipt that pays it, so the owner checks by eye which invoices are
still open. A missing Ukrainian version or a missing requisite weakens the invoice as the primary
document for the income the app already tracks.

## Solution

The app keeps the client's details and the owner's own invoicing details, and produces a bilingual
English/Ukrainian PDF invoice ready to send. A draft can be edited freely; issuing it assigns the
next number and freezes it. The invoice knows when it is paid: the owner links the receipt, and a
monobank receipt that matches an open invoice is offered as the match on the review screen. The app
does not send email; the owner downloads the PDF and sends it.

Legal basis (research recorded 2026-09-29, sources in the Further Notes): a service export may be
concluded by issuing an invoice (Law 959-XII art. 6); payment is acceptance when the offer says so
(Civil Code art. 642(2)); a paid invoice carrying the primary-document requisites of Law 996 art. 9
can support the operation without a separate act (MinFin letters of 2017); primary documents must be
in Ukrainian or carry an authentic Ukrainian translation (MinFin Regulation No. 88), hence bilingual.

## User Stories

Clients

1. As a FOP, I want to add a client's legal name, address, country, VAT or tax id and email, so that invoices carry the buyer's details without retyping.
2. As a FOP, I want clients created by receipts (name only) to gain details later, so that history and new invoices share one client.
3. As a FOP, I want a default currency per client, so that a new invoice starts in the right currency.
4. As a FOP, I want to see and edit my clients on one screen, so that a changed address is fixed once.
5. As a FOP, I want renaming a client to keep its receipts and invoices linked, so that history does not split.
6. As a FOP, I want a client with invoices or receipts to be undeletable, so that issued documents never lose their buyer.

My invoicing details

7. As a FOP, I want to enter my name in Ukrainian and in Latin letters, my RNOKPP and my address in both languages, so that the seller block is complete.
8. As a FOP, I want payment details per currency (IBAN, beneficiary bank, SWIFT, intermediary bank name, SWIFT and account), so that a USD invoice shows USD details and a EUR invoice shows EUR details.
9. As a FOP, I want the intermediary bank fields to be optional free text I copy from my bank app, so that the app never hard-codes details that the bank may change.
10. As a FOP, I want to upload an image of my signature, so that the PDF carries it; without one the invoice shows my name as the identifying data.
11. As a FOP, I want to edit the acceptance, fees and tax-status clauses with sensible bilingual defaults, so that the wording is mine but I never start from a blank page.
12. As a FOP, I want issuing blocked with a clear list of what is missing when my details are incomplete, so that I never send an invoice without a required requisite.

Drafts and issuing

13. As a FOP, I want to create a draft invoice for a client with issue date, due date, currency and lines, so that I can prepare it before sending.
14. As a FOP, I want each line to have an English and a Ukrainian description, a unit from a fixed bilingual list (service, hour, day, month), a quantity and a rate, so that the content and volume of the operation are stated in both languages.
15. As a FOP, I want line amounts and the total computed exactly in the invoice currency, so that the PDF never shows a rounding mismatch.
16. As a FOP, I want to duplicate a previous invoice as a new draft, so that a monthly invoice takes seconds.
17. As a FOP, I want to preview the PDF of a draft, so that I see exactly what the client will get.
18. As a FOP, I want issuing to assign the next number of the year in the form YYYY-NNN, so that numbers are unique and sequential without gaps from abandoned drafts.
19. As a FOP, I want an issued invoice's number, parties, lines and total to be frozen, so that the document I sent is the document I keep.
20. As a FOP, I want to delete a draft, so that abandoned drafts do not clutter the list.
21. As a FOP, I want to cancel an issued invoice with a reason instead of deleting it, so that its number stays accounted for.
22. As a FOP, I want to download the PDF of an issued invoice at any time and get the same document, so that a resend matches the original.

The PDF

23. As a FOP, I want an A4 bilingual invoice with a title, number and dates, seller and buyer blocks, a lines table, the total with the currency, payment details for that currency, the clauses and my signature or name, so that it satisfies the art. 9 requisites.
24. As a FOP, I want the payment narrative to include the invoice number, so that the incoming transfer can be matched to it.
25. As a FOP, I want the PDF file named after the invoice number and client, so that it is easy to attach.

Payment

26. As a FOP, I want to see each invoice's status (draft, issued, overdue, paid, cancelled) and the amount still due, so that I know what to chase.
27. As a FOP, I want to link one or more receipts in the invoice currency to an issued invoice, so that it becomes paid when they cover the total.
28. As a FOP, I want to link from the receipt side too, choosing among the client's open invoices, so that I can do it where I notice the money.
29. As a FOP, I want a receipt linked to an invoice to show the invoice number, so that the receipts list and exports carry it.
30. As a FOP, I want unlinking to reopen the invoice, so that a mistaken link is harmless.
31. As a FOP, I want an imported monobank receipt that matches an open invoice (same currency and amount, and the invoice number or client in the payment details) offered as that invoice's payment on the review screen, so that paid status needs one click.
32. As a FOP, I want a receipt refunded in full to reopen the invoice it paid, so that the status stays true.
33. As a FOP, I want overdue issued invoices counted on the home screen, so that I notice unpaid work.

Data safety

34. As a FOP, I want invoices, client details, my invoicing details and the signature image in the backup, so that a restore brings back my documents.
35. As a FOP, I want invoices written to the change log, so that edits to drafts and status changes have history.
36. As a future second user, I want my clients, invoices and details isolated, so that the product can open up to other FOPs.

## Implementation Decisions

- Modules: Clients (extended from the name-only entity), Invoicing details (part of Settings), Invoices (new feature: entity, endpoints, numbering, PDF), links from Transactions, and a matching suggestion in the monobank import's review flow.
- Client gains legal address, country, tax or VAT id, email, default currency, notes. Name stays unique per owner; the existing create-on-first-use rule for receipts stays.
- Invoicing details belong to the owner's settings: seller name in Ukrainian and Latin, RNOKPP, address in both languages, a list of payment details keyed by currency, clause texts in both languages with defaults, an optional signature image (PNG or JPEG, size-capped, stored in the database, served only to its owner).
- Invoice: owner, client, status Draft | Issued | Cancelled, number (null while a draft), issue date, due date, currency (UAH, USD, EUR), lines (English and Ukrainian description, unit, quantity with up to three decimals, rate in minor units), total in minor units, cancel reason, and a frozen snapshot of seller, buyer, payment details and clauses taken at issue. Paid and overdue are derived, not stored: paid when linked receipts in the invoice currency sum to at least the total; overdue when issued, not paid, and past the due date in Kyiv.
- Issuing is one operation under the owner's advisory lock: validate completeness, take the next number for the issue date's year (max existing plus one, unique index on owner and number), write the snapshot, set Issued. Issuing an already issued invoice is a no-op.
- The PDF is rendered from the frozen snapshot with the existing MigraDoc setup and embedded Noto Sans, so a later change of details or client never alters an issued document. Drafts render from live data with a DRAFT watermark.
- Transaction gains a nullable invoice link. The existing free-text invoice number stays for receipts without an app invoice; linking fills it from the invoice. A receipt may pay only an issued, not cancelled invoice of the same owner and currency.
- The matching suggestion reuses the review screen from #78. An imported receipt whose currency and amount equal an open invoice's outstanding amount, with the invoice number or the client's name found in the counterparty or description, carries a suggested invoice. Confirming links; it never links automatically.
- Backup gains clients' details, invoices with lines and snapshots, invoicing details and the signature image; schema version bumps with older backups still restorable.
- API: CRUD for clients' details; get/put invoicing details and signature upload/delete; invoices list with filters (status, client, year), get, create draft, update draft, delete draft, duplicate, issue, cancel, PDF download; link and unlink a receipt. Types generated from OpenAPI as everywhere else.
- Web: a Clients screen, an Invoicing section in settings, an Invoices list and editor with PDF preview, link controls on the invoice and on the receipt, the suggestion on the review screen, and the overdue count on the home screen. uk and ru strings.
- Docs: knowledge/domain-model.md for Client, Invoice and the links; knowledge/business-rules.md gains an invoicing rule (numbering, freezing, paid and overdue, bilingual requirement, retention of at least 1095 days after the declaration covering it, extended by martial-law suspension); an ADR for the frozen snapshot and bilingual PDF.

## Testing Decisions

- A good test drives the public HTTP API and checks what the owner would see: statuses, numbers, amounts due, the PDF's text. It never asserts on tables or internal calls.
- The seam is the existing API harness (the real app over a PostgreSQL container, fake time via TimeProvider). PDFs are read back with PdfPig, as the receipts PDF tests already do. No new seam.
- Covered: numbering is sequential per year and gap-free under concurrent issues; drafts are editable and issued invoices are frozen; later edits to details or client do not change an issued PDF; missing requisites block issuing with field errors; the PDF contains both languages, both parties, the currency's payment details, the clauses and the total; paid and overdue derive correctly across partial payments, unlinks and refunds; currency mismatch and cross-owner links are rejected; the review suggestion appears only for an exact open match and never links by itself; backup round trip including the signature; owner isolation.
- Prior art: the receipts PDF tests (PdfPig), the transactions and payments endpoint tests, the backup coverage tests, the monobank sync tests' FakeBank for the suggestion.
- UI is proved in a real browser with the verify-taxes-ua skill at 375 px in uk and ru.

## Out of Scope

- Sending email to the client; the owner downloads and sends the PDF.
- Qualified electronic signatures (KEP) on the PDF.
- VAT invoices, domestic acts of acceptance, recurring schedules, multiple templates or branding.
- Automatic linking without the owner's confirmation.
- A document archive and retention reminders (separate backlog item).
- PrivatBank.

## Further Notes

- Sources fetched during research: https://zakon.rada.gov.ua/laws/show/996-14, https://zakon.rada.gov.ua/laws/show/435-15, https://protocol.ua/ru/pro_zovnishnoekonomichnu_diyalnist_stattya_6/, https://7eminar.ua/news/22092-pervinni-dokumenti-fop-it-shho-maje-buti-v-aktax-invoisax, https://7eminar.ua/news/19106-ci-mozna-skladati-invois-lise-angliiskoyu-movoyu, https://yankiv.com/vse-shho-vy-hotily-znaty-pro-invojs/, https://protocol.ua/ua/podatkoviy_kodeks_ukraini_stattya_44/, https://medoc.ua/blog/minimalnij-strok-zberigannja-dokumentiv-5-rokiv-do-jakih-dokumentiv-zastosovutsja-, https://www.universalbank.com.ua/storage/app/media/currency%20new/rekviziti-dlya-perekaziv-v-usdeurchf-gbpplncad.pdf. Official tax.gov.ua pages returned 403 to automated fetches; several points rest on secondary sources.
- monobank's beneficiary bank is JSC Universal Bank (SWIFT UNJSUAUKXXX); intermediary banks are published only in the app, which is why they are owner-entered.
- The default clause texts are a starting point, not legal advice; the owner reviews them once.
- No statutory numbering scheme exists; YYYY-NNN is a choice for uniqueness and order.
