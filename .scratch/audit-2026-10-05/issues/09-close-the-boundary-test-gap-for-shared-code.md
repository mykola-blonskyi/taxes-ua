GitHub: #246
Status: ready-for-agent
Blocked by: none

# Close the boundary-test gap for shared code

## Parent

#237

## What to build

`CrossSiteGuard.cs:44` (shared code) reads `MonobankWebhooks.PathPrefix` from the Monobank feature and the boundary test passes: the constant scan covers only `Features/`, and the shared-code check matches two namespace names. #232 also moved `NotificationChannel` and its retry policy into Settings for one email-prefill read (`DeclarationDetailsEndpoints.cs:242-246`).

## Acceptance criteria

- [ ] The test scans shared files and treats anything outside `Features/` as shared; it fails on today's code first.
- [ ] The webhook path constant lives where shared code may read it.
- [ ] The email prefill reads channels the web already loads; Notifications types move back to Notifications.
- [ ] `docs/architecture.md` states the rule the test enforces.

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-05-full-audit.md, Architecture F2, F3.
