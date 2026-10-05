GitHub: #255
Status: closed
Blocked by: none

# Use a __Host- session cookie with a shorter lifetime

## Parent

#237

## What to build

Owner said yes on 2026-10-05 (#252) to the untriaged L3 of 2026-10-02. The cookie `taxesua.auth` (`Program.cs` cookie setup) has no `__Host-` prefix, so a sibling `*.blonskyi.dev` app can toss a same-named cookie; it lives 14 days sliding, and a bank token sits behind it.

## Acceptance criteria

- [ ] Cookie renamed to `__Host-taxesua.auth`; `web/src/proxy.ts` and any test that names it follow.
- [ ] `ExpireTimeSpan` 7 days, sliding; recorded in ADR-009.
- [ ] An API test asserts the Set-Cookie name, Secure, Path=/ and no Domain.

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-02-full-audit.md, Security L3.
