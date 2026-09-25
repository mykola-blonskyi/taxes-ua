# 12: CSV and XLSX export

GitHub: #13
Status: ready-for-agent
Blocked by: #6

## Parent

#1

## What to build

The owner downloads the year's receipts as CSV and XLSX, with date, amount, currency, rate, rate date, hryvnia amount, type, client and comment, to hand to an accountant.

## Acceptance criteria

- [ ] Both files open cleanly in Excel and Numbers; Cyrillic doesn't break (a BOM for CSV).
- [ ] Amounts in the files are in hryvnia with two decimals and match the screen.
- [ ] API test for the column layout and row count.

## Blocked by

- #6 (Currency receipts and the NBU rate)
