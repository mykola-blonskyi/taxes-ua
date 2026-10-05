GitHub: #261
Status: closed
Blocked by: none

# Expire the 2026 military-levy account by default and ask for the 2027 one

## Parent

#237

## What to build

Owner decision 2026-10-05 (#252). The military levy is paid into temporary Treasury accounts from 2026-07-01 to 2026-12-31 (Law 4908-IX, Rule 16). Today an account the app learned from a payment has no end until the owner taps one in settings. If the owner never taps it, the Pay panel offers the closed 2026 account for the Q4 levy, which is due in February 2027, and the money goes to a closed account.

The new behaviour:

- **A default end.** A military-levy account in use that has no end gets the year's temporary-account end as its default: 2026-12-31 for 2026. This applies to an account learned or entered while that year's temporary period runs, and to accounts that already exist (by migration). The date comes from the tax year's parameters, not from code, so a later year can carry its own end or none.
- **The owner's word wins.** An end the owner set stays as it is. The owner can remove an end, for when the Treasury keeps the account in 2027.
- **A prompt from January.** Once the account has expired, the dashboard shows one notice: "enter the 2027 military-levy account from the Electronic Cabinet", with a link to the settings tab. A reminder through the connected channels (Telegram, email) comes in early January.
- The `ExpiresBeforeDue` and `Expired` states of Rule 16 are unchanged. A Q4 2026 levy paid in December still pays into the account. Paid in February, it shows `Expired` with no QR.

## Acceptance criteria

- [ ] The tax-year parameters carry an optional "temporary military-levy account end". 2026 is seeded with 2026-12-31, and other years have none. The tax-years tab lets the owner edit it.
- [ ] A learned or manual military-levy account with no end, in use during a year that has this date, reports that date as its end, marked as the default. A migration covers existing accounts.
- [ ] An end the owner set is never overwritten. Removing the end works and survives a backup and restore.
- [ ] From the day after the end, the dashboard shows the notice with a link to the Treasury accounts settings, and the Pay panel behaves as `Expired`.
- [ ] One reminder per channel goes out on the first working day after the end, deduplicated like the other reminders.
- [ ] Rule 16 describes the default and the prompt, and the old "applies nothing by itself" sentence is gone.
- [ ] API tests run with the clock at 2026-12-31 and 2027-01-04; a component test covers the notice; e2e follows the treasury-expiry flow.

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-05-full-audit.md, backend finding 5 (#173 follow-up).
