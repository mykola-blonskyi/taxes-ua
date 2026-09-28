# 04: Backfill the year from monobank within the rate limit

GitHub: #77
Status: ready-for-agent
Blocked by: #76
Parent: #71

## What to build

The first sync after connecting walks statement windows (at most 31 days + 1 hour) forward from the registration date, one call per method per 60 seconds, with pacing driven by `TimeProvider`. Each account keeps a cursor at the end of its last fully imported window, so a restart or redeploy resumes where it stopped. Settings shows the month each account has reached and the last sync time. A 429 waits and retries. A 401 or 403 marks the connection broken, stops that token's work and asks the owner for a new token.

## Acceptance criteria

- [ ] A year of backfill issues consecutive, non-overlapping windows and never two calls to one method within 60 seconds (asserted on the stub's recorded requests with a fake clock).
- [ ] Killing the app mid-backfill and starting it again resumes from the cursor without re-importing or skipping.
- [ ] 429 retries after the wait; no error reaches the owner.
- [ ] 401 stops syncing and shows a broken-connection state in settings; replacing the token resumes.
- [ ] Progress and last-sync status proved in a real browser.
