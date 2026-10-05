GitHub: #250
Status: closed
Blocked by: none

# Tidy web Lows from the 2026-10-05 audit

## Parent

#237

## What to build

Items listed in the report's web section: the declaration screen is not keyed by quarter; the missing-tax-year card links to the FOP tab, not `?tab=taxYears`; a settings link to the open URL does not switch tab; the tax-year verified date uses the browser time zone; two audit field labels are missing; the history filter omits three entities; `formatPlainAmount` mishandles negatives; serwist would precache future static pages.

## Acceptance criteria

- [ ] Each item fixed with a test where it has behaviour.

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-05-full-audit.md, Web Lows.
