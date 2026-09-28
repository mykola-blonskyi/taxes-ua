# 01: Share transaction recording between the form and imports

GitHub: #74
Status: ready-for-agent
Blocked by: none
Parent: #71

## What to build

Recording a transaction moves out of the create endpoint into one shared operation: validation, the Kyiv "no future date" rule, the NBU rate lookup (Rule 2), client resolution by name, the before-registration flag. The transaction form and, later, the monobank import both write through it, so Rule 1 and Rule 2 live in one place. No behaviour changes.

## Acceptance criteria

- [ ] Creating a transaction through the API behaves exactly as before; the existing transaction, currency and audit tests pass unchanged.
- [ ] The shared operation is callable without an HTTP request (a background worker can use it).
- [ ] Nothing else duplicates the rate or client rules.
