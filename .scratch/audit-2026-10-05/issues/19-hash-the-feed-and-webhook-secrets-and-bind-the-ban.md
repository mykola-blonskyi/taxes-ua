GitHub: #256
Status: ready-for-agent
Blocked by: #246, #247

# Hash the feed and webhook secrets and bind the bank token to its owner

## Parent

#237

## What to build

Owner said yes on 2026-10-05 (#252) to the untriaged L9 of 2026-10-02. The calendar-feed and monobank-webhook path secrets are stored as drawn (`CalendarFeed.cs`, `MonobankWebhooks.cs`), so a DB dump yields working URLs. The token's AES-GCM has no associated data tying it to `UserId` (`TokenEncryptor.cs`).

## Acceptance criteria

- [ ] Feed and webhook secrets are stored as SHA-256; the full URL is shown only when created or rotated; a migration hashes existing ones without breaking the live URLs.
- [ ] The token is encrypted with `UserId` as associated data; existing tokens are re-encrypted by migration or on next read.
- [ ] The key ring stays as ADR-010 accepts it; the ADR notes the decision.

## Blocked by

- #247
- #246

Details: reports/audits/2026-10-02-full-audit.md, Security L9.
