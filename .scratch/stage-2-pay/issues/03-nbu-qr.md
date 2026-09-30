# 03: Add an NBU QR code to the payment panel

GitHub: #100
Status: ready-for-agent
Blocked by: #99
Parent: #97

## What to build

The Pay panel shows a QR code in the NBU credit-transfer format 003 (Resolution No. 97 of 19.08.2025, as amended by No. 128) for the same details: `https://qr.bank.gov.ua/` plus the Base64URL of the LF-separated block (BCD, 003, 1 for UTF-8, UCT, empty reserved line, recipient name ≤140, IBAN, amount as `UAH` in shortest form, recipient code ≤10, category/purpose code, empty reference, purpose ≤420, display text, field-lock mask `FEFF` so only the amount stays editable). The encoded block stays ≤507 bytes, error correction M or Q, QR version ≤17, with the hryvnia sign in a white circle in the centre as the standard requires. An enlarge button makes it easy to scan from a laptop screen. Editing the amount rebuilds the QR. Copy buttons stay.

## Acceptance criteria

- [ ] A pure payload builder with golden tests, including Cyrillic text, the byte limit, and the shortest amount form (UAH3, UAH1234.5).
- [ ] The rendered QR decodes back to the exact payload (test with a QR decoder in the browser or a unit test).
- [ ] The ISO 20022 category/purpose code for taxes is one constant; start with the research's best candidate and settle it with the owner's real scan.
- [ ] One new web dependency for rendering, justified in the PR.
- [ ] The owner scans it once with monobank on a real device before merge: record whether the app prefills recipient, IBAN, code, amount and purpose, and whether it splits `101` into the payment-type field. If budget payments are refused, keep the QR hidden for Treasury accounts behind a clear note and rely on copy buttons.
- [ ] Proved in a real browser at 375 px in uk and ru.
