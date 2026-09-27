# 06: Deadline calendar

GitHub: #7
Status: closed, done (engine half PR #23/#24; API and web half PR #39)
Blocked by: #4

## Parent

#1

## What to build

The "Quarters and deadlines" screen shows, for every quarter of the year, the ESV, declaration and EP/VZ payment dates, with shifting off weekends and holidays. The engine computes statutory and shifted dates from the year's parameters and the FOP's settings: the ESV day, declaration day count, days to pay after the declaration, weekends, holidays, whether the payment deadline counts from the statutory date, whether the payment deadline itself shifts. Changing settings updates the table immediately.

## Acceptance criteria

- [x] An engine table-driven test reproduces the entire 2026 reference table: 2026-04-20/2026-05-11/2026-05-20, 2026-07-20/2026-08-10/2026-08-19, 2026-10-19/2026-11-09/2026-11-19, 2027-01-19/2027-02-09/2027-02-19.
- [x] Tests for both values of every shifting flag and for a holiday from the list.
- [x] Q4 returns next year's dates; quarters before the registration date are not shown.
- [x] The screen displays the shifted date with a hint showing the statutory one when they differ.

New criterion, from independent verification of the engine:

- [x] `ForQuarter` is never called with an `EsvDeadlineDay` that cannot exist in the month after a quarter. Met by #4's bound (1..28); PR #39 adds the matching guard against a settings row marking all seven days as weekend.

## Blocked by

- #4 (Year parameters and FOP settings)
