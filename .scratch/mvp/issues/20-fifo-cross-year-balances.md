# 20: Allocate payments to the oldest debt first and carry balances across years

GitHub: #47
Status: closed, done (PR #55). Landed inside `Balances.ForYears`; there is no separate
`ObligationBuilder.cs`.
Blocked by: #9

## What to build

Two owner decisions made on 2026-09-27 change how Rule 7 allocates payments. Both live in the
engine's `Balances`, so they ship together.

1. **Oldest debt first.** Within one payment kind (single tax, military levy, ESV), money paid is
   applied to the oldest outstanding obligation first, whatever quarter or month the payment
   names. This is how the tax office credits payments against debt (Tax Code art. 87.9; the owner
   accepted this reading). Example: Q1 levy 1,000.00 unpaid, Q2 levy 1,000.00 accrued, 2,000.00
   paid "for Q2". Both Q1 and Q2 show Done, not Q1 Overdue forever.
2. **Overpayment carries across years.** An overpayment of one kind at the end of year Y is
   carried into year Y+1 for the same kind. It is never applied to another kind. Debt carries
   forward too: an unpaid Y balance is still owed in Y+1.

The period a payment names is kept and shown. It no longer decides which obligation the payment
settles.

## Acceptance criteria

- [x] Engine: `Balances` allocates payments FIFO by due date within a kind, across years, and
  never across kinds. Tests cover:
  - the Q1/Q2 example above
  - a partial payment splitting across two obligations
  - an overpayment in December 2026 covering Q1 2027
  - an unpaid 2026 Q4 still Overdue in 2027
  - an overpayment of one kind while owing another (unchanged: the other kind stays owed)
  - no registration date (unchanged: nothing accrues)
- [x] API: `GET /api/periods/{year}` obligations and year balances reflect the allocation,
  including the opening balance carried in from earlier years. Obligations stay per kind and per
  quarter. The API still does not recompute tax; it loads the needed years through
  `YearAccruals.LoadAsync` or its successor.
- [x] `knowledge/business-rules.md` Rule 7 records both decisions and removes the "to confirm"
  notes they settle.
- [x] The Payments and Periods screens show the new figures. Verified live at 375 and 1280.

## Blocked by

- #9
