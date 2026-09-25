# 03: Year parameters and FOP settings

GitHub: #4
Status: ready-for-agent
Blocked by: #3

## Parent

#1

## What to build

In settings, the owner sets the FOP registration date, the payment mode, the ESV registration-month policy, the ESV exemption, and the deadline-shifting rules. A separate tab shows TaxYearConfig by year: minimum wage, rates in basis points, the limit, deadline day counts, holidays, source and verification date. 2026 arrives via seed data. A year can be cloned into the next one and marked verified. The home screen warns when the current year has no verified config.

## Acceptance criteria

- [ ] After startup, an empty database has a 2026 row with the values from business-rules; migrations run at startup.
- [ ] `EsvMonthlyKop` and `IncomeLimitKop` are recomputed from the minimum wage on save and equal 190,234 and 1,009,104,900 for 2026.
- [ ] Cloning 2026 into 2027 creates a row with `VerifiedAt` empty, and the home screen shows a warning for 2027.
- [ ] Every Settings field from domain-model is available in the form and persists.
- [ ] API tests for read, write, clone, and marking a year verified.

## Blocked by

- #3 (Google sign-in and the interface shell)
