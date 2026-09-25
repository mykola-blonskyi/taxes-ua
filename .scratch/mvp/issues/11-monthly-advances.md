# 11: Monthly advances

GitHub: #12
Status: ready-for-agent
Blocked by: #10

## Parent

#1

## What to build

In "monthly ahead" mode, the owner sees a "By month" screen with income, EP, VZ, ESV and the recommended advance due by the 15th of the following month, records a payment with a "month" period, and the advance is credited against the quarterly obligation. The home screen's next step accounts for advances. The remainder to pay or an overpayment is visible in the quarters view.

## Acceptance criteria

- [ ] Engine tests: an advance larger than the quarterly accrual yields an overpayment; a partial advance yields a remainder; switching modes never changes accruals, only recommendations.
- [ ] The "By month" screen appears only in advance mode and matches the prototype's column layout.
- [ ] A payment with a month is visible in the balances of the quarter that month belongs to.

## Blocked by

- #10 (Home screen: the next step)
