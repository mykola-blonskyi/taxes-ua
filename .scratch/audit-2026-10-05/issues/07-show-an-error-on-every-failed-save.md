GitHub: #244
Status: closed
Blocked by: none

# Show an error on every failed save

## Parent

#237

## What to build

About ten mutation sites show a message only for an `ApiError`; a dropped connection just re-enables the button, so the owner may think it saved. `PasskeyButton.tsx:39-46` has the same gap. Mark-paid drops the API's reason.

## Acceptance criteria

- [ ] Every mutation shows a translated message for network failures and for API errors, with the API's code where it has one.
- [ ] Tests cover a network failure on at least two forms.

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-05-full-audit.md, Web M6 and the mark-paid Low.
