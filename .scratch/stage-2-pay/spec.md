GitHub: #97
Status: ready-for-agent

# Spec: Pay each obligation with ready details and an NBU QR, and track a tax reserve

## Problem Statement

The app tells the owner what to pay, how much and by when, and after #80 it recognises the payment in the bank statement. The payment itself is still typed by hand: the Treasury IBAN for the right kind and region, the recipient, the amount, and a payment purpose in the format the Treasury requires. A wrong IBAN or purpose sends money to the wrong kind or leaves it unallocated, which is exactly the kind of error the app exists to prevent. The owner also has no view of whether the money set aside for taxes will cover the next deadlines.

## Solution

Each obligation on the home screen gets a Pay action. It shows the Treasury account for that kind, the recipient, the amount and a ready payment purpose, each with a copy button, and a QR code in the NBU credit-transfer format that a banking app can scan to prefill the transfer. The owner only confirms in the bank. The app never initiates a payment.

The Treasury accounts come from the owner's own confirmed payments: when a bank operation is confirmed as a budget payment (#80), its recipient becomes the account for that kind. Until the first confirmation, the owner enters them once.

A tax reserve view shows, after each receipt, how much of it to set aside, and in total how much the open and accruing obligations need. If the owner keeps a monobank jar for taxes, the app reads its balance and shows the shortfall or surplus.

## User Stories

Treasury accounts

1. As a FOP, I want the Treasury account for each kind (single tax, military levy, ESV) to be remembered from the last payment I confirmed from the bank, so that I never look it up again.
2. As a FOP, I want to enter or correct a Treasury account by hand (IBAN, recipient name, recipient code), so that I can pay before the app has seen any payment.
3. As a FOP, I want a hand-entered account to win over a learned one until I change it, so that my correction is not overwritten by an old payment.
4. As a FOP, I want to see where each account came from (learned on a date, or entered), so that I trust it.
5. As a FOP, I want the app to warn me when a newly confirmed payment went to a different account than the one on record, so that I notice when the Treasury changed accounts (as it did for the military levy on 1 July 2026).
6. As a FOP, I want an IBAN that is not a Treasury account rejected, so that I cannot save a typo.

Paying

7. As a FOP, I want a Pay action on every obligation the home screen shows (due, overdue, or an advance), so that paying starts where I see the debt.
8. As a FOP, I want the Pay panel to show the account, recipient, recipient code, amount and purpose, each with a copy button, so that I can paste them into any bank.
9. As a FOP, I want the amount to be what is owed now for that kind and period, so that I pay exactly the debt.
10. As a FOP, I want to change the amount before copying (for example to pay ahead), so that the panel serves partial and advance payments too.
11. As a FOP, I want the payment purpose built in the format the Treasury requires (the payment type code 101 followed by the kind and period in Ukrainian words), so that the payment is allocated to the right obligation.
12. As a FOP, I want a QR code in the NBU credit-transfer format for the same details, so that my banking app prefills the transfer when I scan it from another screen.
13. As a FOP, I want the QR code to be large enough to scan from a laptop screen with my phone, and a button to enlarge it, so that scanning works first time.
14. As a FOP, I want the panel to say clearly that the app does not pay and that I confirm in the bank, so that there is no doubt about who moves money.
15. As a FOP, I want the panel to tell me what is missing (the Treasury account or its recipient code) with a link to fix it, instead of a broken QR, so that I am never given a wrong code.
16. As a FOP, I want a paid obligation to disappear from the panel after the bank operation is confirmed (#80), so that the loop closes without typing the payment.
17. As a FOP, I want the Pay action on the payments screen too, per kind and period, so that I can pay an older period or ahead.

Tax reserve

18. As a FOP, I want each receipt to show how much of it to set aside for single tax and military levy, so that I move the right amount right away.
19. As a FOP, I want a total "needed for taxes" figure: everything still unpaid that has accrued, including the current quarter to date and ESV to date, so that I know what the reserve must hold.
20. As a FOP, I want the figure split by nearest deadlines, so that I know what must be there by which date.
21. As a FOP, I want to pick one of my monobank jars as the tax reserve, so that the app compares it with what is needed.
22. As a FOP, I want the jar's balance refreshed on every sync and on demand, respecting monobank's rate limit, so that the comparison is current.
23. As a FOP, I want a clear shortfall ("top up 3 400 ₴ before 19 November") or surplus, so that I act on one number.
24. As a FOP, I want only UAH jars offered, so that the comparison needs no conversion.
25. As a FOP, I want the reserve view to work without monobank or without a jar, showing only what is needed, so that it is useful on its own.
26. As a FOP, I want the jar's name and balance never sent anywhere but my own screen, so that personal savings stay private.

Data

27. As a FOP, I want the Treasury accounts and the jar choice in the backup, so that a restore keeps them.
28. As a future second user, I want all of this isolated by owner.

## Implementation Decisions

- Modules: Treasury accounts (new, per owner and kind), the Pay panel (web, fed by the home screen's next step and the payments screen), a payment-details builder (pure functions for the purpose text and the NBU QR payload), the tax reserve (engine-side figures plus a jar reader in the monobank adapter), settings for the reserve jar.
- Treasury account: owner, kind (EP, VZ, ESV), IBAN, recipient name, recipient code, source (Learned with the operation and date it came from, or Manual), updated at. One row per owner and kind. A confirmation from #80 upserts the row as Learned unless a Manual row exists; a confirmation to a different IBAN than a Manual row raises a visible notice instead of overwriting. The recipient name and code are taken from the confirmed operation (the statement carries the counterparty name and code for FOP accounts); where missing, the owner fills them.
- IBAN validation reuses the Treasury recognition from #80 (bank id 899998) plus the mod-97 check.
- The purpose text and the QR payload are built by pure functions, with golden tests. The purpose follows MinFin Order No. 148 of 22.03.2023 (structured format, in force for Treasury accounts since 01.12.2023): the payment type code (101 for current obligations) and free additional information, e.g. `101 єдиний податок за III квартал 2026 року`, `101 військовий збір за III квартал 2026 року`, `101 єдиний внесок за III квартал 2026 року`, and for an advance the month instead of the quarter. The payer's RNOKPP is not part of the purpose: it travels as the payer's code in the transfer. The old `*;101;<РНОКПП>;…;;;` format (Order 666, repealed) is never produced. One transfer per kind, as the Order requires.
- The QR payload follows NBU Resolution No. 97 of 19.08.2025 (amended by No. 128, in force since 01.11.2025), format 003: the URL `https://qr.bank.gov.ua/` followed by the Base64URL of an LF-separated block: `BCD`, `003`, `1` (UTF-8), `UCT`, an empty reserved line, recipient name (≤140), IBAN, amount as `UAH` plus the shortest decimal form, recipient code (≤10), category/purpose code, empty reference, the purpose (≤420), display text, and a field-lock mask; the encoded block stays ≤507 bytes; error correction M or Q; QR version ≤17; the hryvnia sign in a white circle in the centre as the standard requires. The ISO 20022 category/purpose code for taxes is unconfirmed (the text's only example is `SUPP/SUPP`); the builder takes it from one constant, and the ticket settles it by testing with the owner's banking app. The field-lock mask leaves the amount editable (`FEFF`). The server returns the payload string with the other details; the web renders it with a small, widely used QR library (one new dependency, justified in the PR). Copy buttons are always shown: monobank is reported compliant with the NBU format, but whether any app splits the leading `101` into the structured payment-type field when the QR targets a Treasury account is unverified and needs one real test by the owner.
- The recipient code (EDRPOU) is mandatory in the QR and in the transfer: the panel refuses to build a QR without it and asks for it.
- Amounts: the Pay panel starts from the next step's amount per kind (Rule 7 allocation, including monthly advances in that payment mode) and lets the owner edit it before building the purpose and QR; the QR is rebuilt on edit.
- Tax reserve figures are computed by the engine from data it already has: per receipt, its hryvnia amount times the single tax and military levy rates of its year; in total, every accrued and unpaid amount per kind as the allocation reports it, plus the current quarter's accruals to date, grouped by due date. No new tax rule is introduced.
- The jar is read from monobank client-info (jars: id, title, currency, balance, goal) through the existing rate gate for that method, on each sync run and on an explicit refresh. The chosen jar id and its last balance with the fetch time are stored per owner. Only UAH jars can be chosen.
- API: get and put Treasury accounts; get payment details for a kind, period and amount (account, recipient, purpose, QR payload, or the list of what is missing); get the reserve figures; list jars (a fresh read through the gate) and choose one; refresh the balance. Types generated from OpenAPI.
- Web: a Pay button on each debt on the home screen and on the payments screen, opening a panel (a sheet on phones); a Treasury accounts section in settings; a reserve card on the home screen and a set-aside line on imported and manual receipts. uk and ru strings.
- Docs: knowledge/business-rules.md gains the Treasury account and reserve rules; domain model updated; an ADR records that the app prepares payments and never initiates them, and why the QR is offered alongside copy buttons.

## Testing Decisions

- Good tests drive the HTTP API and read what the owner would see (the panel's fields, the QR payload, the reserve figures, the jar comparison), plus golden tests for the two pure builders.
- Seams: the existing API harness (real app, PostgreSQL container, FakeTimeProvider, the FakeBank for monobank including jars). The builders are pure functions tested directly, like the engine. No new seam.
- Covered: learned account from a confirmation; manual wins; the different-IBAN notice; invalid IBAN rejected; panel fields and missing-data list; purpose and QR payload for each kind, quarter and month (advances), with Cyrillic text and the length limits; amount edit rebuilds the payload; reserve per receipt and in total across a quarter boundary and with payments allocated; jar listing filters non-UAH; balance refresh goes through the rate gate; shortfall and surplus; everything owner-isolated; backup round trip.
- Prior art: the dashboard and periods endpoint tests, the payment candidate tests from #80, the monobank sync tests' FakeBank, the engine's table tests.
- The QR is proved in a real browser and scanned once with a real banking app by the owner; the verification skill covers 375 px in uk and ru.

## Out of Scope

- Initiating or signing a payment, or any write to a bank.
- Moving money into the jar; the owner sets up monobank's own auto top-up if wanted.
- Payment details for anything other than the single tax, military levy and ESV.
- Reminders, the declaration readiness checklist and the XML declaration (separate specs).

## Further Notes

- Treasury accounts change by region and over time (new military levy accounts from 1 July 2026, research in #80), which is why they are learned from real payments rather than shipped as a table.
- The ESV recipient is the regional tax authority on a non-budget account, and the single tax and military levy recipient is the regional Treasury; both are captured per kind from the confirmed operation.
- Research sources (fetched 2026-09-30): https://zakon.rada.gov.ua/laws/show/v0097500-25 (NBU Resolution 97), https://zakon.rada.gov.ua/laws/show/z0528-23 (MinFin Order 148), https://zakon.rada.gov.ua/laws/show/z0974-15 (Order 666, repealed), https://github.com/opencartbot/nbu-qr-v3 and https://github.com/sirkadirov/uabankpay.js (implementations and a banking-app compliance table, secondary). bank.gov.ua and tax.gov.ua returned 403 to automated fetches.
