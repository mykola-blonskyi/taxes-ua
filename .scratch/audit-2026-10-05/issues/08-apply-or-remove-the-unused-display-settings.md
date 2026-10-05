GitHub: #245
Status: closed
Blocked by: none

# Apply or remove the unused display settings

## Parent

#237

## What to build

Settings language, theme and default currency are saved but nothing in the UI reads them (`useSettings()` has one caller). Language and theme already follow the shell's own choices.

## Acceptance criteria

- [ ] Owner decides: apply them as defaults on sign-in, relabel them as reminder settings, or delete them.
- [ ] Then implement the decision with tests.

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-05-full-audit.md, Web M7.
