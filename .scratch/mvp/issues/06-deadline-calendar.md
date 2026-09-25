# 06: Deadline calendar

GitHub: #7
Status: ready-for-agent (engine half done on main, API and web half remain)
Blocked by: #4

## Parent

#1

## What to build

The "Quarters and deadlines" screen shows, for every quarter of the year, the ESV, declaration and EP/VZ payment dates, with shifting off weekends and holidays. The engine computes statutory and shifted dates from the year's parameters and the FOP's settings: the ESV day, declaration day count, days to pay after the declaration, weekends, holidays, whether the payment deadline counts from the statutory date, whether the payment deadline itself shifts. Changing settings updates the table immediately.

## Acceptance criteria

- [ ] An engine table-driven test reproduces the entire 2026 reference table: 2026-04-20/2026-05-11/2026-05-20, 2026-07-20/2026-08-10/2026-08-19, 2026-10-19/2026-11-09/2026-11-19, 2027-01-19/2027-02-09/2027-02-19.
- [ ] Tests for both values of every shifting flag and for a holiday from the list.
- [ ] Q4 returns next year's dates; quarters before the registration date are not shown.
- [ ] The screen displays the shifted date with a hint showing the statutory one when they differ.

## Blocked by

- #4 (Year parameters and FOP settings)
