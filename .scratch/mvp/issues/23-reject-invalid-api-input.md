# 23: Reject comma-joined enum values and NUL characters at the API boundary

GitHub: #53
Status: closed, done (PR #58)
Blocked by: #9, #14, #48

## What to fix

The #14 lane (PR #50) found two inputs that the existing POST and PUT endpoints for transactions,
payments and settings answer with 500 instead of 400.

1. **Comma-joined enum values.** The global `JsonStringEnumConverter` accepts flags-style input
   such as `"currency": "USD, EUR"` or `"kind": "Income, RefundToClient"` on non-flags enums. The
   value is stored as an undefined enum member, and every later read of that row answers 500. So
   one bad request can break the owner's screens until the row is removed by hand.
2. **NUL characters in text.** A `\^@` in any text field (client name, description, invoice
   number, reason) makes PostgreSQL reject the insert, and the API answers 500.

Restore (#14) rejects both inside its own path, but the shared validators should reject them too,
once, at the boundary.

## Acceptance criteria

- [x] An enum value that is not a single defined member is rejected with 400 on every endpoint
  that binds an enum. Prefer one converter or binding policy at the boundary over per-endpoint
  checks.
- [x] A NUL character in any text field is rejected with 400 (or stripped, if there is a
  documented reason), consistently across endpoints.
- [x] Api tests cover both inputs on each affected endpoint. If #14 has merged, restore's own
  checks are simplified to rely on the shared rule.

## Blocked by

- #9
- #14
- #48
