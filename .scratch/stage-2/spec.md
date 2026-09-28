GitHub: #71
Status: ready-for-agent

# Spec: Sync FOP accounts from monobank

## Problem Statement

The owner keeps every receipt by hand. Each foreign-currency payment from a client has to be typed
into the app with its date, amount and currency, and each budget payment has to be recorded again
after it was already made in the bank. That is slow, and a missed or mistyped receipt silently
understates income, which is the one error the tax office punishes.

All the owner's FOP accounts are in monobank: UAH, USD and EUR. monobank publishes a personal API
that returns these accounts and their statements. On 2026-09-28 the owner confirmed with their own
token that the FOP accounts come back as `type: "fop"`. PrivatBank is deferred: the owner has no FOP
account there.

## Solution

The owner connects monobank once by pasting a personal API token in settings and choosing which FOP
accounts to follow. From then on the app pulls every operation on those accounts by itself: a full
backfill from the registration date, near-instant updates when the bank reports a new operation,
and a nightly catch-up.

Incoming money becomes a transaction with a suggested kind (income, own transfer, currency sale and
so on) and the NBU rate fixed as for a manual entry. It waits in a review list until the owner
confirms or changes the kind. An outgoing payment to the Treasury becomes a budget payment
candidate; the owner confirms its kind and period, and it lands in the payment ledger.

The app still never moves money. The monobank personal API is read only, and the token cannot pay.

## User Stories

Connecting

1. As a FOP, I want to paste my monobank personal API token in settings, so that the app can read my FOP accounts without me typing statements.
2. As a FOP, I want the app to check the token the moment I save it, so that I learn about a typo immediately rather than at the first sync.
3. As a FOP, I want to see the accounts the token exposes, with currency and a masked IBAN, so that I can tell which is which.
4. As a FOP, I want only my FOP accounts offered for sync by default, so that personal card spending never lands among my receipts.
5. As a FOP, I want to choose which FOP accounts to follow, so that an account I do not use for business stays out.
6. As a FOP, I want accounts of types the app does not know (for example `diia`) to be listed as not supported rather than break the connection, so that a new account type in the bank does not stop my sync.
7. As a FOP, I want the token stored encrypted and never shown back to me, so that a leaked database or backup does not expose read access to all my bank accounts.
8. As a FOP, I want to replace the token, so that I can rotate it after revoking the old one in the bank.
9. As a FOP, I want to disconnect monobank, so that the app deletes the token and stops syncing while keeping what was already imported.
10. As a FOP, I want to see when each account last synced and whether the last sync failed, so that I trust the numbers on the home screen.

Syncing

11. As a FOP, I want the first sync to pull everything from my registration date, so that the year's income is complete without a manual backfill.
12. As a FOP, I want the backfill to continue by itself across restarts and redeploys, so that a long first import does not need me to start it again.
13. As a FOP, I want to see the backfill's progress (which month it has reached), so that I know when the year is complete.
14. As a FOP, I want a new operation to appear within a minute or two of the bank reporting it, so that the home screen reflects a payment I just received.
15. As a FOP, I want a nightly catch-up over the last 31 days, so that an operation missed by the bank's notification still arrives.
16. As a FOP, I want a "sync now" button, so that I can pull fresh data before checking my numbers.
17. As a FOP, I want repeated syncs never to create a duplicate, so that the same bank operation counts once however many times it is fetched.
18. As a FOP, I want an operation that is still on hold in the bank to be left out until it settles, so that a reversed authorisation never counts as income.
19. As a FOP, I want each operation dated by its calendar date in Kyiv, so that a payment credited just after midnight UTC lands in the right quarter.
20. As a FOP, I want an operation dated in the future (clock skew) to be rejected the same way a manual entry is, so that Rule 1 holds for imports too.
21. As a FOP, I want a sync that hits the bank's rate limit to wait and continue, so that I never see an error for something the app can simply retry.
22. As a FOP, I want a revoked or expired token to stop syncing and ask me for a new one, so that the app does not keep hammering the bank with a dead token.
23. As a FOP, I want each sync run recorded with how many operations were imported and skipped, so that I can see what happened.

Receipts

24. As a FOP, I want a foreign-currency receipt converted at the NBU rate on its credit date, exactly as a manual entry, so that imported and typed receipts agree.
25. As a FOP, I want the counterparty's name kept on the imported transaction, so that I can recognise the client.
26. As a FOP, I want the client linked by counterparty name, creating it the first time, so that receipts from one client group together as they do for manual entries.
27. As a FOP, I want the bank's description and my own transfer comment kept, so that I can find the invoice a receipt pays.
28. As a FOP, I want an incoming payment from an outside counterparty suggested as income, so that the common case needs one confirmation.
29. As a FOP, I want a credit from one of my own accounts suggested as an own transfer, so that moving money between my accounts is not taxed.
30. As a FOP, I want a hryvnia credit from selling my own currency suggested as a currency sale, so that it is not counted twice.
31. As a FOP, I want every imported transaction to start as "needs review", so that nothing is classified for me without my confirmation.
32. As a FOP, I want a review list of everything waiting, newest first, so that I can clear it in one sitting.
33. As a FOP, I want to confirm a suggested kind with one action, so that reviewing a normal month takes seconds.
34. As a FOP, I want to change the kind and give a reason for non-income, so that Rule 1 is satisfied for imports as it is for manual entries.
35. As a FOP, I want an unreviewed receipt to count toward income under its suggested kind, so that the home screen never understates what I owe while I have not reviewed yet.
36. As a FOP, I want the home screen to warn me when transactions await review, so that I do not rely on figures that include unconfirmed classifications.
37. As a FOP, I want my edits to an imported transaction to survive every later sync, so that the bank never overwrites my decision or its fixed rate.
38. As a FOP, I want deleting an imported transaction to keep it from coming back on the next sync, so that a dismissed operation stays dismissed.
39. As a FOP, I want imported transactions to show their source (monobank, which account), so that I can tell them apart from manual entries.
40. As a FOP, I want outgoing operations other than budget payments ignored, so that business spending, which is not deductible, does not clutter my ledger.

Budget payments

41. As a FOP, I want a payment from my FOP account to a Treasury account recognised as a budget payment candidate, so that I do not record it twice.
42. As a FOP, I want the candidate's kind suggested from the payment purpose (single tax, military levy, ESV), so that most candidates need only a confirmation.
43. As a FOP, I want the candidate's period prefilled with the oldest open obligation of that kind, as the home screen does, so that the default follows Rule 7.
44. As a FOP, I want to confirm a candidate, which creates the budget payment, so that the balances and the next step update.
45. As a FOP, I want to dismiss a candidate that is not a tax payment, so that it disappears for good.
46. As a FOP, I want a confirmed payment linked to its bank operation, so that the same operation never becomes a second payment.
47. As a FOP, I want a payment I already typed by hand to be offered as the match for a candidate with the same date, kind and amount, so that I do not end up with the payment twice.

Safety and data

48. As a FOP, I want the backup to include imported transactions with their bank ids but never the token, so that a restore followed by a sync does not duplicate anything and the backup file carries no bank access.
49. As a FOP, I want every import to write the usual audit entries, so that an imported row has the same history as a typed one.
50. As a FOP, I want the bank's notification address to be unguessable, so that nobody else can make the app hit the bank on my behalf.
51. As a FOP, I want notification requests to only trigger a sync and never be trusted as data, so that a forged notification cannot insert a transaction.
52. As a future second user, I want my token, accounts and imports isolated by owner, so that the product can open up to other FOPs later.

## Implementation Decisions

Scope and vocabulary

- Stage 2 bank integration is monobank only. PrivatBank stays in the domain enums but gets no code.
- No payments are created in the bank. The monobank personal API has no endpoint for it.

monobank adapter (boundary)

- One typed HTTP client for `client-info`, `statement/{account}/{from}/{to}` and `webhook`, registered like the NBU client so tests can replace its primary handler.
- It parses responses into domain records at the boundary. The time in Unix seconds becomes a Kyiv `ValueDate` plus the original `BankTime`. Amounts stay in minor units. The currency comes from the ISO 4217 numeric code; a currency outside UAH, USD, EUR skips the item and counts it as skipped.
- The account `type` is read as an open string. Only `fop` is offered for sync. Any other value, known or not, is listed as not supported and never fails parsing.
- Items with `hold: true` are skipped. A later sync picks them up once settled.
- The adapter holds no business rules. Classification and matching happen after parsing, in plain functions over the parsed records.

Rate limiting and scheduling

- Each monobank method allows 1 request per 60 seconds. One pacing gate per token and method, driven by `TimeProvider`, so tests advance time instead of waiting.
- A statement window is at most 31 days plus 1 hour. Backfill walks windows forward from the registration date.
- Sync work runs in one background worker (`IHostedService`). The "sync now" endpoint, the webhook and the nightly timer only enqueue work for an account; they never call the bank on the request thread.
- Each synced account keeps a cursor: the end of the last fully imported window. A restart resumes from the cursor. The nightly run re-reads the last 31 days whatever the cursor says.
- 429 waits and retries. 401 or 403 marks the connection broken, stops that token's work and surfaces on settings. Other failures retry on the next scheduled run.

Idempotency

- A transaction's `ExternalId` is the monobank operation `id`, unique together with `BankAccountId`, as the domain model already specifies.
- Sync only inserts. A row whose `ExternalId` exists is never updated, so owner edits and the fixed rate (Rule 2) win over the bank.
- Deleting an imported transaction keeps a tombstone (the row with `ReviewStatus = Dismissed`, excluded from every figure) so the next sync does not recreate it. Manual transactions delete as today.

Schema changes

- `BankAccount` as in the domain model: `Bank`, `Name`, `Currency`, `Iban`, `IsFop`, `ExternalId` (the monobank account id), `IsActive`, plus the sync cursor and last-sync status. The encrypted token belongs to the monobank connection, one per owner, not to each account.
- `Transaction` gains `BankAccountId?`, `ExternalId?`, `BankTime?`, `Counterparty?`, `ImportBatchId?`, `ReviewStatus` (`Confirmed | NeedsReview | Dismissed`; existing and manual rows are `Confirmed`).
- `ImportBatch` as in the domain model, one per account per sync run.
- `BudgetPayment` gains `ExternalId?` and `BankAccountId?`, unique together, for a payment confirmed from a candidate.
- A budget payment candidate: the parsed outgoing operation, the suggested kind, and a status (`Pending | Confirmed | Dismissed`). Unique by account and `ExternalId`.

Classification (pure functions)

- An incoming item whose counterparty IBAN is one of the owner's own known accounts: `OwnTransfer`. A UAH credit that pairs with a same-moment foreign-currency debit on the owner's own FOP account: `FxSale`. Every other incoming item: `Income`.
- Every imported transaction starts as `NeedsReview`. It counts toward income under its current kind, so the figures never understate; the dashboard carries a warning while any row needs review.
- Confirming sets `Confirmed`. Changing to a non-income kind requires the reason, as for manual entries.
- The client is resolved by counterparty name with the existing create-on-first-use rule.

Budget payment candidates

- An outgoing item whose counterparty IBAN is a Treasury account becomes a candidate. The Treasury is recognised from the bank code inside the IBAN (899998).
- The suggested kind comes from the payment purpose text. With no confident match the kind is left empty and the owner picks it.
- The period is prefilled with the oldest open obligation of that kind, the same default the home screen uses (Rule 7). The owner can change it.
- Confirming creates a `BudgetPayment` carrying the operation's `ExternalId`. Before creating it, a manual payment with the same date, kind and amount and no `ExternalId` is offered as the match; accepting links it instead of creating a second one.

Token and security

- The token is encrypted with AES-256-GCM under a key from the environment, as `docs/architecture.md` already states. It deliberately does not use the Data Protection key ring: ADR-009 rotates that ring to end every session, and that must not also destroy the token.
- The token is write-only through the API. Responses show only whether a token is set and its status.
- The token is never in a backup. A restore keeps the connection absent; the owner reconnects and the sync dedupes by `ExternalId`.
- The webhook URL carries a per-owner random secret in its path. The handler answers 200 at once, ignores the body, and enqueues a sync of the owner's accounts. An unknown secret gets 404. The webhook is registered only when a public base URL is configured, so a local stack runs without it.

API contracts (all under the existing authorised `/api` surface, except the webhook)

- Put or replace the token; delete the connection; read the connection status and accounts; choose which accounts to follow.
- Enqueue a sync now; read per-account sync status and the latest import batches.
- List transactions filtered by review status; confirm or reclassify through the existing transaction update.
- List budget payment candidates; confirm (with kind, period, optional match) or dismiss.
- Webhook: an anonymous GET that answers 200 (the bank validates the URL with it) and an anonymous POST, both under the secret path.

Web

- Settings gains a monobank section: token field, accounts with checkboxes, per-account status and "sync now".
- A review screen lists transactions needing review and pending budget payment candidates.
- The dashboard shows the review warning with a link to the review screen.
- Types come from OpenAPI as for every other screen.

Documentation

- `knowledge/domain-model.md` records the new fields, `ReviewStatus`, the candidate entity and the connection.
- `knowledge/business-rules.md` gains the import rules: hold items are skipped, unreviewed rows count, sync never overwrites, deletion leaves a tombstone.
- `docs/decisions.md` records the token-key decision and the webhook-as-signal decision.

## Testing Decisions

- A good test drives the app through its public HTTP API and asserts what the owner would see (transactions, balances, the next step, statuses), never internal tables or method calls.
- The seam is the existing API test harness: the real app over a real PostgreSQL container, with monobank replaced at the HTTP message handler level and the clock replaced through `TimeProvider`. This is the pattern the NBU client already uses, so it adds one stub and no new seam. The stub records requests so tests can assert on windows and pacing.
- Covered behaviour: connecting with a good and a bad token; account listing with an unknown type; backfill across several windows with pacing; resume after restart from the cursor; no duplicates on repeated and overlapping syncs; hold items skipped then imported once settled; Kyiv date conversion across midnight; foreign-currency receipts converted as manual ones; owner edits surviving a later sync; deletion tombstones; 429 retry; 401 breaking the connection; webhook secret accepted and rejected; forged webhook body ignored; Treasury candidate confirm, match to a manual payment, and dismiss; backup without token and restore then sync without duplicates; isolation between two owners.
- Classification and candidate recognition are pure functions and get table-driven unit tests next to the API tests, like the engine's tests.
- Prior art: the NBU client tests with their stub handler and fake time; the transactions, payments, backup and import endpoint tests; the audit log tests for imported rows.
- UI is proved in a real browser with the project's verification skill, including 375px width, before the ticket closes.

## Out of Scope

- PrivatBank in any form (statement files and the Autoclient API).
- CSV/XLSX statement import. It is a separate Stage 2 item.
- Creating payments in the bank, and any payment QR code or payment details screen.
- Filing the declaration or anything touching the Electronic Cabinet.
- Personal (non-FOP) monobank accounts and jars.
- The accountant view of monobank (`managedClients`).
- Refunds to clients from the imported outflows. The owner records a refund by hand as today.
- Reminders through Telegram or email.

## Further Notes

- The owner's token was checked on 2026-09-28: three `fop` accounts (UAH, USD, EUR) plus `eAid`, `diia` and `black` personal accounts. `diia` is missing from the published OpenAPI enum, which is why the type is parsed as an open string.
- The published spec describes no signature on webhook requests. That is why the webhook is a signal and not a data source.
- A full year of backfill for three accounts is at least 36 statement calls, so over 36 minutes at one call per minute. The progress indicator exists for this reason.
- The Treasury bank code and the payment-purpose keywords should be checked against a real tax payment in the owner's statement before the classifier is finalised.
