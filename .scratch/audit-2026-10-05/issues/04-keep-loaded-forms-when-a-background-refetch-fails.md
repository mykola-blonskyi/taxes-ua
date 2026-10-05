GitHub: #241
Status: closed
Blocked by: none

# Keep loaded forms when a background refetch fails

## Parent

#237

## What to build

Nine settings forms and the invoice editor guard on `query.isError || !data` (e.g. `FopSettingsForm.tsx:93`, `InvoicesScreen.tsx:35`). React Query sets `isError` after a failed background refetch while data is still present, so a network blip or a focus refetch replaces the form with "load failed" and drops unsaved edits.

## Acceptance criteria

- [ ] Forms render while data exists, whatever the refetch state; the error screen shows only with no data.
- [ ] A component test: data loaded, edit a field, refetch fails, the edit is still there.

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-05-full-audit.md, Web M2.
