# 06: Pick up new monobank operations by webhook and nightly catch-up

GitHub: #79
Status: ready-for-agent
Blocked by: #77
Parent: #71

## What to build

A new operation shows up within a minute or two. When a public base URL is configured the app registers a webhook at a per-owner secret path. The bank's validation GET gets 200; a POST answers 200 at once, ignores its body and enqueues a sync of that owner's accounts. An unknown secret gets 404. A nightly job re-reads the last 31 days for every connected account whatever the cursor says, so a webhook the bank disabled after three failed deliveries loses nothing.

## Acceptance criteria

- [ ] With no public base URL configured, no webhook is registered and the local stack runs as before.
- [ ] A POST to the right secret path enqueues a sync; to a wrong one returns 404 and enqueues nothing.
- [ ] A forged POST body carrying a fake operation inserts nothing; only the statement read does.
- [ ] The nightly run imports an operation the webhook never announced.
- [ ] ADR in `docs/decisions.md`: webhook as signal, not data source (the spec describes no signature).
