GitHub: #253
Status: ready-for-agent
Blocked by: none

# Delete the prototype importer

## Parent

#237

## What to build

Owner decision 2026-10-05 (#252): the importer was never used. `PrototypeFile.cs` says its format was guessed because no sample exists. Remove it with its endpoint, problem codes, tests, the `PrototypeImportPanel` UI and its messages.

## Acceptance criteria

- [ ] No prototype import code, route, problem code, message key or test remains (`grep -ri prototype` over api/src and web/src is empty apart from unrelated words).
- [ ] Regenerated `schema.d.ts` has no import route; the architecture and layout tests pass.
- [ ] Docs and ADRs that describe the importer say it was removed and why.

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-05-full-audit.md, Architecture F5.
