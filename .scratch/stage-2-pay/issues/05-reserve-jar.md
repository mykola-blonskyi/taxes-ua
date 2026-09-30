# 05: Track a monobank jar as the tax reserve

GitHub: #102
Status: ready-for-agent
Blocked by: #101
Parent: #97

## What to build

The owner picks one of their monobank jars as the tax reserve (only UAH jars are offered). The app reads jars from client-info through the existing rate gate on every sync run and on an explicit refresh, stores the chosen jar and its last balance with the fetch time, and the reserve card shows the balance next to what is needed, with the shortfall and the date it is needed by, or the surplus. The jar's name and balance are shown only to the owner.

## Acceptance criteria

- [ ] FakeBank tests: jar listing filters non-UAH; the balance refresh goes through the rate gate and never calls client-info twice within 60 s; shortfall and surplus; stale balance shows its time.
- [ ] Choice survives backup and restore; owner isolation.
- [ ] Proved in a real browser with a monobank stub at 375 px in uk and ru.
