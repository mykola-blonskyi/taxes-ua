# 02: Connect monobank with a personal API token

GitHub: #75
Status: ready-for-agent
Blocked by: none
Parent: #71

## What to build

In settings the owner pastes a monobank personal API token. The app checks it against `client-info` at once, stores it encrypted with AES-256-GCM under a key from the environment (not the Data Protection ring, which ADR-009 rotates to end sessions), and never returns it. The owner sees the token's accounts with currency and masked IBAN. FOP accounts are checked for sync by default; any other type, including ones missing from the published enum such as `diia`, is listed as not supported. The owner can replace the token or disconnect, which deletes the token and keeps imported data.

## Acceptance criteria

- [ ] A valid token saves and lists accounts; an invalid one is rejected with a field error and nothing is stored.
- [ ] The account type is parsed as an open string; an unknown type never fails the request.
- [ ] The token never appears in any API response, log line or backup.
- [ ] Choosing accounts persists `BankAccount` rows per owner; a second owner sees none of them.
- [ ] Disconnect removes the token; `BankAccount` rows stay.
- [ ] monobank is stubbed at the HTTP handler in tests, like the NBU client.
- [ ] ADR in `docs/decisions.md` for the token key; `knowledge/domain-model.md` updated.
- [ ] Settings section proved in a real browser, including 375px width.
