# Architecture Analysis

Observations from the compiled API on 2026-10-04 (#185) and the 2026-10-02 audit
(`reports/audits/2026-10-02-full-audit.md`). The design itself is in `docs/architecture.md`; the
feature edges are in [dependencies.md](dependencies.md).

## Components

- `TaxesUa.Engine`: pure tax arithmetic over input records. No package, no clock, no `decimal`,
  `double` or `float`. Every rate, day count and threshold comes from `TaxYearConfig`.
- `TaxesUa.Api`: one ASP.NET Core assembly with 19 feature namespaces under `Features/`, layered 0 to 7
  with no cycle. The bottom holds owner data and reference data (`Auth`, `Fx`, `TaxYears`, `Banking`,
  `Settings`); the middle holds the ledgers (`Transactions`, `Payments`, `Periods`, `Invoices`,
  `Export`, `Monobank`, `Declarations`); the top holds what reads across all of them (`Dashboard`,
  `Notifications`, `Audit`, `Backup`, `Calendar`, `Clients`). Shared code sits in `TaxesUa.Api`
  (`Problems`, `TextRules`, `KyivTime`, `Incident`) and `TaxesUa.Api.Data` (`AppDbContext`,
  `OwnerLock`, migrations).
- Hosted workers inside the api: reminders every 5 minutes, the monobank sync queue and nightly sync,
  Telegram polling.
- `web`: Next.js, `app` → `features` → `data` → `shared`, talking to the api only through the generated
  OpenAPI types.
- `backup`: a compose sidecar that dumps `taxes_ua` nightly and proves a restore weekly (ADR-031).

## Risks

- Backup and restore (`Features/Backup`) touch every owner table. A new entity the backup forgets is
  lost on restore; one reflection test guards it. `BackupDocument.cs` is 1,800 lines (#186).
- The monobank sync keeps its queue, rate gate and client-info cache in process memory, so the api must
  run as one instance. It mixes `ExecuteUpdate` with tracked entities, by design, outside the audit log.
- The money ledger (engine `Accruals`, `Balances`, `IncomeLedger` and the api's `Periods`) carries the
  highest correctness stakes. It is pure and well tested, but a rule change reaches the dashboard,
  reminders, declarations and the reserve.
- Reminders claim before they send, so a crash mid-send loses that reminder rather than doubling it.
- The boundary test reads IL, so a cross-feature `const` is invisible to it.

## Improvements

- #186 splits `BackupDocument.cs`, `InvoicesEndpoints.cs`, `TransactionsEndpoints.cs` and
  `InvoicingEndpoints.cs`.
- Several endpoints still answer a bare 404 with no body (Treasury accounts, invoicing signature,
  declaration files). The audit asked for the coded `Problems.NotFound` everywhere except the secret
  paths; that changes responses, so it is left for its own ticket.
- `TransactionsEndpoints.MinYear` and `MaxYear` are read by five features. They belong in shared code.
