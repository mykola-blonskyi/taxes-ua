# 03: Connect email for reminders

GitHub: #107
Status: ready-for-agent
Blocked by: none
Parent: #104

## What to build

An email channel. With SMTP settings configured (host, port, TLS, user, password, from), the owner adds an address and confirms it through a signed, expiring link; unconfirmed addresses receive nothing but the confirmation. Toggle, test message, remove, retries and a visible failure as for Telegram. Without SMTP settings the channel shows as unavailable.

## Acceptance criteria

- [ ] In-memory SMTP sender tests: confirmation link (valid, expired, tampered), test message in plain text and simple HTML, retries, failure shown.
- [ ] Credentials only in configuration and never logged; docs/deploy.md documents the variables.
- [ ] Settings proved in a real browser at 375 px in uk and ru.
