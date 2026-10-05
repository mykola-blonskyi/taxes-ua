GitHub: #237

## Problem Statement

The owner relies on the app to know what to pay, how much and when. The full audit of 2026-10-05 (`reports/audits/2026-10-05-full-audit.md`) found no Critical issue, but found ways the app can fail, show a wrong picture, or lose data without the owner noticing:

- **The dashboard crashes** with a 500 when the year's group 3 income is negative. A January refund of a December receipt, before any new income, produces exactly that. The 85% limit warning also fires about 1,000 UAH early, because it compares a rounded percentage instead of kopecks.
- **A foreign-currency bank receipt can be lost for good.** When the NBU rate lookup fails, the sync skips the receipt and still moves its position forward. Only a short re-read window retries it, so a failure during a backfill or after a long sync gap understates income permanently.
- **Two write paths skip the owner lock.** Two refunds submitted at once can together exceed the receipt they refund, and a typed payment can race a bank-candidate confirm and be recorded twice.
- **The web can lose or misstate data:**
  - a failed background refresh wipes unsaved edits in settings and invoice forms;
  - signing out leaves the previous session's data in the cache;
  - the dashboard stays stale after a bank sync or an invoice change;
  - a settings form left open across a restore writes the old values back;
  - a network failure on save shows nothing, so the owner thinks it saved;
  - three saved settings (language, theme, default currency) have no effect.
- **Engineering and operational gaps:**
  - the module boundary test misses constants that shared code reads from a feature;
  - Notifications types drifted into Settings;
  - tests run on an older Postgres than production;
  - deploy-time database dumps are not encrypted;
  - the weekly restore check trusts any newer file and restores it as a superuser;
  - a bank account identifier is still logged;
  - docs drifted from the code.
- **Owner decisions from the audit:**
  - the prototype importer was never used and carries about 1,200 lines;
  - the JSON backup schema reached v18 with an upgrade step per version;
  - three older Low security findings were never ticketed (session cookie prefix and lifetime, plain secrets at rest, tax-year parameters writable by any signed-in user);
  - the host firewall does not limit web traffic to Cloudflare.

## Solution

The owner sees a dashboard that always opens and a limit bar that is exact to the kopeck. Every bank receipt is recorded, even when the NBU was briefly down. Repeated or concurrent submissions record once. Forms keep what the owner typed, every failure is shown, and every change refreshes what it affects. Behind this:

- the boundary test covers shared code;
- tests run on the production database version;
- dumps and backups are encrypted and authenticated;
- dead code is deleted;
- the backup format stops growing a version per field;
- the session cookie and stored secrets are hardened;
- only an admin can change the tax-year parameters.

## User Stories

1. As the owner, I want the dashboard to open after I refund last year's receipt in January, so that I am never locked out of my figures.
2. As the owner, I want the limit bar to show none of the limit used when the year is negative, so that the bar never draws nonsense.
3. As the owner, I want the 85% warning to fire exactly at 85% of the limit, so that I am not alarmed 1,000 UAH early.
4. As the owner, I want "remaining before the warning" to count down smoothly to zero, so that the figure never jumps by millions.
5. As the owner, I want every foreign-currency bank receipt recorded even when the NBU rate was briefly unavailable, so that my income is never understated.
6. As the owner, I want a sync that hit a rate failure to retry that window on its next run, so that a temporary outage fixes itself.
7. As the owner, I want a retried window never to record the same receipt twice, so that income is never overstated.
8. As the owner, I want a double-tapped refund recorded once, so that refunds never exceed the receipt.
9. As the owner, I want a payment I type in and a bank candidate I confirm for the same transfer never both recorded, so that my ledger matches the bank.
10. As the owner, I want a restore never interleaved with a new transaction or payment, so that the restored state is exactly the backup.
11. As the owner, I want my unsaved edits in settings to survive a brief network failure, so that I do not retype them.
12. As the owner, I want the invoice draft I am editing to survive a brief network failure, so that I do not lose an invoice.
13. As the owner, I want to see an error screen only when there is no data to show, so that a refresh hiccup does not hide my form.
14. As the owner, I want signing out to clear everything the app cached, so that the next person at the device sees nothing of mine.
15. As the owner, I want signing in to start from an empty cache, so that I never see another account's figures.
16. As the owner, I want an expired session to send me to sign-in once, so that I am not left on a page of failed requests.
17. As the owner, I want the dashboard current right after a bank sync, so that what to pay reflects the new receipts.
18. As the owner, I want the dashboard's overdue-invoice notice current right after I issue, cancel or delete an invoice.
19. As the owner, I want invoices to show a client's new name right after I rename the client.
20. As the owner, I want an open settings form to show the restored values after a restore, so that pressing Save does not undo the restore.
21. As the owner, I want a clear message in my language when a save fails because the network dropped, so that I know it did not save.
22. As the owner, I want the reason the server gave when marking a payment paid fails, so that I can correct it.
23. As the owner, I want a passkey sign-in that fails on the network to tell me so.
24. As the owner, I want every setting I can change to have a visible effect, or not to be offered.
25. As the owner, I want deploy-time database dumps encrypted like the nightly backup, so that a copy of the server's disk does not expose my history.
26. As the owner, I want only a few deploy-time dumps kept, so that old copies do not pile up.
27. As the owner, I want the weekly restore check to ignore a backup dated in the future, so that a planted file cannot pass as the newest.
28. As the owner, I want the restore check to run with the least privilege, so that a malicious file cannot run commands on the server.
29. As the owner, I want my bank account identifiers kept out of logs.
30. As the owner, I want a reminder in flight during a deploy still delivered, so that a release never silently drops a reminder.
31. As the owner, I want database dumps and verification screenshots never committable by accident to the public repo.
32. As the owner, I want the prototype importer and its settings panel gone, since I never used it.
33. As the owner, I want a clear message telling me to download a fresh backup when I try to restore one from an older format.
34. As the owner, I want a fresh backup downloadable right after the format change, so that I always hold a restorable file.
35. As the owner, I want my session cookie impossible for a sibling site under the same domain to overwrite.
36. As the owner, I want my session to expire after a week of inactivity, since a bank token sits behind it.
37. As the owner, I want my calendar-feed and bank-webhook links not to be recoverable from a database dump.
38. As the owner, I want my encrypted bank token bound to my account, so that it cannot be swapped onto another row.
39. As the owner, I want only an admin able to change, verify or clone a tax year's parameters, so that adding a second person later cannot change my figures.
40. As the owner, I want my site reachable only through Cloudflare, so that its protections cannot be bypassed.
41. As a maintainer, I want the module boundary test to fail when shared code reads a feature's constant, so that the documented boundary is the enforced one.
42. As a maintainer, I want Notifications types to live in Notifications, so that Settings does not become a shared dumping ground.
43. As a maintainer, I want tests to run on the same Postgres major version as production, so that a migration that passes CI also passes in production.
44. As a maintainer, I want an additive backup field to need no schema version bump, so that the upgrade chain stops growing.
45. As a maintainer, I want the architecture and graph docs to match the code, so that a new contributor is not misled.
46. As a maintainer, I want the small web inconsistencies fixed (declaration screen state per quarter, the missing-tax-year link, tab switching by link, the Kyiv date for "verified", missing history labels and filters, negative amount formatting), so that each screen behaves predictably.

## Implementation Decisions

- **Limit monitor (engine).** Negative year income is a valid ledger state (refunds are dated by their own day). The engine floors it at zero before evaluating, so every caller (the dashboard, the limit bar) is safe. The warn and exceeded lines are compared in kopecks (`income × 100 ≥ limit × pct`); the rounded basis-point percentage stays for display only. Both the level and "remaining to the next line" use the same comparison. Rule 4 records both. Delivered in PR #259.
- **Owner lock.** Every write that checks a ledger invariant runs in a transaction holding the per-owner advisory lock, the existing mechanism used by update, delete, confirm and restore. The transaction and payment create endpoints join that discipline; no new locking mechanism.
- **Bank sync position.** The sync advances its position only past windows where every receipt was recorded or deliberately skipped. A rate failure leaves the position before the window, and the next run re-reads it. Idempotency comes from the existing uniqueness on the bank operation id.
- **Web query state.** A screen shows its error state only when it has no data. A background refetch failure keeps the rendered form. A session change (sign-out, sign-in, a 401 from any query) clears the entire query cache. Mutations invalidate by the existing hierarchical query keys; the sync, invoice and client mutations widen their invalidation to the dashboard and the lists that show them. Settings forms re-seed their local state when the server data changes identity after a restore.
- **Save errors.** One shared mapping turns any mutation failure into a translated message: API problem codes through the existing code-to-message table, network failures through a new generic message in both locales.
- **Unused display settings.** Awaiting the owner's choice (apply, relabel as reminder settings, or delete). Recommended: delete language and theme from server settings, since the shell already owns them, and keep default currency only if a form reads it.
- **Feature boundaries.** "Shared" means anything outside the feature folders. The boundary test scans shared files for feature constants as well as types. Constants that shared code needs move to shared code. The email prefill reads the channels the web already loads, which lets the Notifications types move home.
- **Test database.** Integration tests, CI and the local stack use the production Postgres major version.
- **Deploy dumps and backups.** Pre-migration dumps are encrypted to the same age recipient as the nightly backup, and retention drops to three. The restore check skips future-dated objects and restores as a non-superuser role. Ignore rules cover dump files and root screenshots.
- **Logging.** Logs name the app's own account id, never the bank's identifier.
- **Reminder sends.** A send uses the channel's own timeout rather than the host's shutdown token, so a deploy does not cancel a reminder mid-send.
- **Prototype importer.** Deleted outright, with its endpoint, problem codes, messages, UI panel and tests. The ADR that introduced it records the removal.
- **Backup schema floor.** v18 is the floor. Older files are refused with a stable problem code and a translated "download a fresh backup" message, and the upgrade steps and fixtures below v18 are deleted. From now on, an optional field with a default restores from a file that lacks it, so it needs no bump. An ADR records the floor and when a bump is still required. The nightly encrypted dump is the disaster backup.
- **Session cookie.** It is renamed with the `__Host-` prefix and has a 7-day sliding lifetime. The web proxy follows the rename. ADR-009 records the change.
- **Secrets at rest.** The feed and webhook path secrets are stored as SHA-256 hashes, and the full URL is shown only when it is created or rotated. Existing rows migrate without breaking live URLs. The bank token's AES-GCM uses the owner's id as associated data, and existing tokens are re-encrypted.
- **Tax-year writes.** Writing, verifying and cloning require an admin. Admins come from configuration, by default the first allowlisted address. Reads are unchanged.
- **Host firewall.** The owner does this on the server. Only Cloudflare's ranges reach the web ports, and the rule sits where Docker-published ports cannot bypass it. Host specifics stay off the public tracker.

## Testing Decisions

- A good test drives the system through its public surface and asserts what the owner or a client would observe: HTTP status and body, the rendered screen, a file in a bucket. Private helpers and query-key shapes are not asserted.
- **Seams, in order of preference (all existing):**
  1. **The HTTP API through the shared API fixture**, against a real Postgres container. Prior art: the dashboard, transactions, payments, monobank sync, backup restore and cross-site guard tests. Concurrency fixes are proven here by firing two requests at once and asserting one outcome.
  2. **Engine unit tests**, only where the rule is pure arithmetic: limit thresholds with the real 2026 limit. Prior art: the limit monitor and limit-crossing tests.
  3. **Web component tests through the shared harness and fetch stub.** These cover refetch failures that keep the form, the cache cleared on sign-out, invalidation after mutations and save-error messages. Prior art: the payment-screen, invoice and settings component tests.
  4. **Playwright owner flows**, only where a flow spans pages: sign-out then sign-in, restore then edit settings. Prior art: the e2e suite with the NBU stub. The 375 px layout check runs on any UI change.
  5. **Architecture tests** for the boundary rule. Prior art: the feature-boundary and clock tests.
  6. **The backup smoke check** for dump encryption and the restore check's refusal of a future-dated object. Prior art: the compose-based backup smoke test in CI.
- **Failing test first.** Every bug ticket lands its failing test as its own commit before the fix.
- **Deletions.** These are proven by the remaining suites staying green and by the regenerated API schema no longer containing the removed routes.

## Out of Scope

- Whether the learned 2026 temporary military-levy account should expire by default on 2026-12-31. The question is with the owner and gets its own ticket once answered.
- Server-side session revocation, including ending a session when an address leaves the allowlist (ADR-009).
- Encrypting the data-protection key ring at rest (ADR-010 accepts it).
- Multi-owner support beyond gating tax-year writes.
- The Stage 3 yearly document archive.
- Rewriting history to remove the origin IP from old commits.

## Further Notes

- Tickets:
  - #238: dashboard and limit, PR #259 open.
  - #239: owner lock.
  - #240: bank sync position.
  - #241–#244: web state and errors.
  - #245: display settings, needs-info.
  - #246: boundaries.
  - #247: Postgres version.
  - #248: dumps and backups.
  - #249–#251: tidies and docs.
  - #252: owner decisions log.
  - #253: importer deletion.
  - #254: backup floor.
  - #255–#257: older Lows.
  - #258: host firewall, an owner action.
- Order:
  1. #238, #239 and #240 first. These are correctness and data loss.
  2. Then #241–#244. These are where the owner sees wrong or lost data.
  3. Then #246–#248 and #253–#257.
  4. The tidies last.
- #254 ships with a release note: download a fresh JSON backup after it deploys.
- Every merge to main deploys to production, so merges wait for an independent review and the owner's go-ahead.
