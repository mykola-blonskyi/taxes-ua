# 07: Accruals and the declaration numbers

GitHub: #8
Status: in review (engine half merged in PR #28; periods endpoint and web tables on branch ticket-8)
Blocked by: #6, #7

## Parent

#1

## What to build

After receipts are entered, the quarters table shows income, EP, VZ, ESV and the total, and the "Declaration numbers" screen shows cumulative income, EP and VZ for the quarter, half-year, nine months and year with the filing deadline. The engine computes income by period with refunds, cumulative EP and VZ minus what was already accrued, and ESV over active months since the registration date per the registration-month policy and the exemption.

## Acceptance criteria

- [x] Engine tests: registration mid-quarter with one and two active months, a full year, ESV exemption, a refund in a different quarter, a refund larger than the month's income, a year rollover.
- [x] The sum of quarterly EP equals cumulative EP for the year to the kopeck.
- [x] The periods API response matches the engine's computation on the same data; the response carries a warning when the year's config isn't verified.
- [x] The tables on screen match the prototype's column layout.

- [x] A refund of a pre-registration receipt does not manufacture a tax credit. The link landed in #41 (PR #43); PR #45 loads it into the periods figures and counts the excluded refund.

## Blocked by

- #6 (Currency receipts and the NBU rate)
- #7 (Deadline calendar)
