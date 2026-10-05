GitHub: #257
Status: closed
Blocked by: none

# Scope tax-year writes to an admin

## Parent

#237

## What to build

Owner said yes on 2026-10-05 (#252) to the untriaged L10 of 2026-10-02. `TaxYearEndpoints.cs` lets any authenticated user PUT, verify or clone the global tax-year parameters. Harmless with one owner; cross-tenant tampering once the allowlist holds a second person.

## Acceptance criteria

- [ ] Writes require an admin email from config (`Auth__AdminEmails`, defaulting to the first allowlisted address); reads stay open to signed-in users.
- [ ] A test: a non-admin allowlisted user gets 403 on PUT, verify and clone.

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-02-full-audit.md, Security L10.
