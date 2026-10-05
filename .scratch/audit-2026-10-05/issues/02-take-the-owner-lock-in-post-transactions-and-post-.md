GitHub: #239
Status: ready-for-agent
Blocked by: none

# Take the owner lock in POST /transactions and POST /payments

## Parent

#237

## What to build

PUT, DELETE and confirm hold `OwnerLock`; the two POSTs do not (`TransactionsEndpoints.cs:83-128` and the payments POST). Two concurrent refund POSTs can both pass the over-refund check (Rule 8); a typed payment can race the bank-candidate confirm and be recorded twice; either can interleave with a restore. The comment at `OwnerLock.cs:6-9` promises otherwise.

## Acceptance criteria

- [ ] Both POSTs run in a transaction holding the owner lock, as PUT does.
- [ ] A test fires two concurrent refunds of one receipt whose sum exceeds it; exactly one succeeds.

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-05-full-audit.md, Backend #4, Architecture F1.
