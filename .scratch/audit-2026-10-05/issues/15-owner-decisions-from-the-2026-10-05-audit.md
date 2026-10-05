GitHub: #252
Status: ready-for-human
Blocked by: none

# Owner decisions from the 2026-10-05 audit

## Parent

#237

## What to build

Calls only the owner can make; each unblocks a deletion or a ticket.

## Acceptance criteria

- [ ] Delete the prototype importer (about 1,200 lines with tests, plus a UI panel, built for a guessed format) if it was never used.
- [ ] Pick a support floor for backup schema versions (now v18) so older upgrade steps can go; new fields get constructor defaults instead of a version bump.
- [ ] Should the learned 2026 temporary military-levy account expire by default on 2026-12-31?
- [ ] Ticket or decline the three untriaged Lows of 2026-10-02: session cookie prefix and sliding expiry, plain secrets at rest, tax-year parameters not owner-scoped.
- [ ] On the host: ports 80/443 Cloudflare-only; `Origin` and `Sec-Fetch-Site` reach the app; whether snapshots include the dump and key-ring volumes.
- [ ] Remove the 31 stale agent worktrees (about 21 GB).

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-05-full-audit.md, Not fixable in code.
