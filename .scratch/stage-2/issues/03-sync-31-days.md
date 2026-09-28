# 03: Sync the last 31 days of monobank FOP receipts

GitHub: #76
Status: ready-for-agent
Blocked by: #74, #75
Parent: #71

## What to build

A "sync now" action enqueues work for the chosen FOP accounts. A background worker reads the last 31 days of statement and records each settled incoming operation through the shared transaction operation: `Kind = Income`, `ReviewStatus = NeedsReview`, `ValueDate` by Kyiv date, `BankTime`, `Counterparty`, client by counterparty name, NBU rate fixed at write time. Items on hold are skipped. The operation `id` is the `ExternalId`, unique per account, so repeated syncs insert nothing new, and an existing row is never updated. Each run is an `ImportBatch` with imported and skipped counts. Imported transactions show their source in the list. The backup carries the new fields and never the token; restore followed by sync creates no duplicates.

## Acceptance criteria

- [ ] Sync imports UAH, USD and EUR receipts with the same amounts and rates a manual entry would get.
- [ ] Running sync twice, or over overlapping windows, adds no rows.
- [ ] An owner edit to an imported row survives the next sync.
- [ ] `hold: true` items are skipped, then imported once the bank reports them settled.
- [ ] An operation just after midnight Kyiv time lands on the Kyiv date.
- [ ] Outgoing operations are ignored (budget payments come in a later ticket).
- [ ] Unreviewed rows count toward income under their kind.
- [ ] Every imported row writes the usual audit `Create` entry.
- [ ] Backup and restore round-trip `ExternalId` and friends; the token is absent from the file.
- [ ] Existing manual rows migrate as `Confirmed`.
- [ ] `knowledge/business-rules.md` states the import rules.
- [ ] "Sync now" and the source marker proved in a real browser.
