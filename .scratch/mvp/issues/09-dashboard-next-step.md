# 09: Home screen: the next step

GitHub: #10
Status: ready-for-agent
Blocked by: #9

## Parent

#1

## What to build

Opening the app, the owner sees the single nearest unfinished step: what to do, by when, how much, how many days remain or how overdue it is, with a button to open the Electronic Cabinet. Below it, the tax burden as a percentage of income. States: before the registration date a hint, all done, overdue in red. The engine determines the next step relative to today's date by Europe/Kyiv.

## Acceptance criteria

- [ ] Engine tests for choosing the next step with several obligations/overdue/an empty list/a date before registration.
- [ ] The dashboard API computes "today" by Kyiv time, not UTC; a test for the midnight boundary.
- [ ] The hero matches the prototype: a large date, the step's name, the amount, the days, the actions.
- [ ] Marking a payment moves the step forward without a reload.

## Blocked by

- #9 (Budget payments and balances)
