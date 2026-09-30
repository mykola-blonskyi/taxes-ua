# 02: Pay each obligation from a panel with ready payment details

GitHub: #99
Status: ready-for-agent
Blocked by: #98
Parent: #97

## What to build

Every debt the home screen shows (due, overdue or an advance) and every kind and period on the payments screen gets a Pay action. It opens a panel (a sheet on phones) with the Treasury account, recipient, recipient code, amount and payment purpose, each with a copy button, and a plain statement that the app does not pay: the owner confirms in the bank. The amount starts from what is owed for that kind and period and can be edited. The purpose is built by a pure function per MinFin Order 148: `101 <kind> за <period>` in Ukrainian words (quarter, or month for an advance), e.g. `101 єдиний податок за III квартал 2026 року`; the RNOKPP is not in it. When the account or recipient code is missing, the panel lists what to fill and links to it. Once the bank operation is confirmed (#80) the debt disappears as today.

## Acceptance criteria

- [ ] Golden tests for the purpose builder: each kind, each quarter, a monthly advance, year boundary.
- [ ] Payment details endpoint returns the fields, or the list of what is missing; owner isolation.
- [ ] Amount edits are validated (positive, kopecks) and reflected in the details.
- [ ] ADR: the app prepares payments and never initiates them.
- [ ] Panel proved in a real browser at 375 px in uk and ru; copy buttons work.
