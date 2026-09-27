# 22: Export the year's receipts as PDF

GitHub: #49
Status: closed, done (PR #57)
Blocked by: #13

## What to build

A PDF export of the year's receipts next to the existing XLSX and CSV links (#13), for printing or
sending to an accountant. Requested by the owner on 2026-09-27.

The content follows the XLSX export: the same rows, the same columns and the same hryvnia
figures, from the same column table in `Features/Export/TransactionExport.cs`. There is a header
with the year and the generation date in Kyiv time, a year total in hryvnia, and page numbers. The
layout is A4 landscape, and Cyrillic renders correctly, which needs an embedded font.

## Acceptance criteria

- [x] `GET /api/export/transactions.pdf?year=` is scoped to the owner, returns 401 when signed out
  and 400 for a bad year.
- [x] The PDF library is permissively licensed (MIT, Apache or BSD), including transitive packages
  and the embedded font's license. Justify the choice in the PR.
- [x] Cyrillic text, negative refund amounts and long client names render correctly. A test checks
  the page count and that the extracted text contains the expected rows and total.
- [x] A PDF link sits beside XLSX and CSV on the receipts screen. Verified live at 375 and 1280.

## Blocked by

- #13
