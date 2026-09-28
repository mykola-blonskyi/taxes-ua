GitHub: #71
Status: needs-triage
Blocked by: none

# Import FOP operations from the monobank personal API

## What

Import FOP account operations from the monobank personal API into `Transaction`, and match
budget payments in the statement to `BudgetPayment`. Stage 2 item from `plans/backlog.md`.

## Confirmed on 2026-09-28

The owner ran `GET /personal/client-info` with their own token. It returns three accounts with
`type: "fop"` (UAH 980, USD 840, EUR 978). It also returns a `type: "diia"` account that the
published OpenAPI enum does not list.

## API facts (from the official spec)

- Read only. `client-info`, `statement/{account}/{from}/{to}`, `webhook`. No payment creation.
- 1 request per 60 seconds per method. A statement window is at most 31 days + 1 hour.
- Statement items carry a unique `id`, `time` in Unix seconds, `amount` in minor units,
  `currencyCode` (ISO 4217), `hold`. FOP accounts also carry `counterIban`, `counterEdrpou`,
  `counterName`, `invoiceId`.
- Webhook: must answer `200` within 5 s. Retries after 60 s and 600 s, then the bank disables it.
  The spec describes no request signature.

## Shape

- One adapter at the boundary parses responses into domain records. Kyiv date from `time`,
  kopecks from minor units, `hold: true` items skipped until settled. Account `type` is an open
  string; unknown values are ignored, never an error.
- Webhook is only a "sync now" signal. Data always comes from `statement`.
- Writes are an upsert keyed by the monobank `id` (`ExternalId`). Webhook, bank retries and a
  nightly 31-day reconciliation converge on the same state.
- Fetches run in an `IHostedService` queue that respects the 60 s limit. A full year is at least
  12 windows per account.
- The token reads every account, personal cards included. Stored encrypted.

## Acceptance

- Selecting FOP accounts imports their operations without duplicates on repeated runs.
- A payment to a Treasury IBAN is matched to the budget payment ledger.
- A disabled webhook does not lose operations; the nightly reconciliation catches up.
