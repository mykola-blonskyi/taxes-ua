# 04: Show how much to set aside for taxes

GitHub: #101
Status: ready-for-agent
Blocked by: none
Parent: #97

## What to build

A tax reserve view. Each receipt shows how much of it to set aside for single tax and military levy (its hryvnia amount times the year's rates). The home screen gets a reserve card: the total needed for taxes, meaning every accrued and unpaid amount per kind as the allocation reports it plus the current quarter's accruals to date, split by due date. It works without monobank.

## Acceptance criteria

- [ ] Engine tests: set-aside per receipt for each year's rates; total needed across a quarter boundary, with payments allocated (Rule 7), with monthly advances (Rule 6), and with ESV to date.
- [ ] Reserve figures endpoint; owner isolation.
- [ ] Receipt set-aside line and home card proved in a real browser at 375 px in uk and ru.
