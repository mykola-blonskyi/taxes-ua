# Architecture Analysis

Observations from the compiled API on 2026-10-04 (#185), the 2026-10-02 audit
(`reports/audits/2026-10-02-full-audit.md`) and the 2026-10-05 audit (`reports/audits/2026-10-05-full-audit.md`). The design itself is in `docs/architecture.md`; the
feature edges are in [dependencies.md](dependencies.md).

## Components

- `TaxesUa.Engine`: pure tax arithmetic over input records. No package, no clock, no `decimal`,
  `double` or `float`. Every rate, day count and threshold comes from `TaxYearConfig`.
- `TaxesUa.Api`: one ASP.NET Core assembly with 19 feature namespaces under `Features/`, layered 0 to 8
  with no cycle. The bottom holds owner data and reference data (`Auth`, `Fx`, `TaxYears`, `Banking`,
  `Settings`); the middle holds the ledgers (`Transactions`, `Payments`, `Periods`, `Invoices`,
  `Export`, `Monobank`, `Declarations`); the top holds what reads across all of them (`Clients`,
  `Dashboard`, `Notifications`, `Audit`, `Calendar`, and `Backup` at the very top). `DatabaseBackups`
  stands alone at the bottom. Shared code sits in `TaxesUa.Api`
  (`Problems`, `TextRules`, `KyivTime`, `Incident`) and `TaxesUa.Api.Data` (`AppDbContext`,
  `OwnerLock`, migrations).
- Hosted workers inside the api: reminders every 5 minutes, the monobank sync queue and nightly sync,
  Telegram polling.
- `web`: Next.js, `app` → `features` → `data` → `shared`, talking to the api only through the generated
  OpenAPI types.
- `backup`: a compose sidecar that dumps `taxes_ua` nightly and proves a restore weekly (ADR-031).

## Risks

- Backup and restore (`Features/Backup`) touch every owner table. A new entity the backup forgets is
  lost on restore; one reflection test guards it. The backup is split across `Backup*.cs` and the `BackupDocument` partials; the
  largest hand-written file in the api is 595 lines, under the 600-line gate.
- The monobank sync keeps its queue, rate gate and client-info cache in process memory, so the api must
  run as one instance. It mixes `ExecuteUpdate` with tracked entities, by design, outside the audit log.
- The money ledger (engine `Accruals`, `Balances`, `IncomeLedger` and the api's `Periods`) carries the
  highest correctness stakes. It is pure and well tested, but a rule change reaches the dashboard,
  reminders, declarations and the reserve.
- Reminders claim before they send, so a crash mid-send loses that reminder rather than doubling it.
- The boundary test reads IL and scans the sources for `Type.Const` reads. A const reached any other
  way (a `using static`, a fully qualified name split across lines) would slip past the scan.

## Improvements

- Files near the 600-line gate: `DeclarationsEndpoints.cs` (595), `InvoicingEndpoints.cs` (577) and
  `NotificationsEndpoints.cs` (560). The next edit to the first needs a split.
- Several endpoints still answer a bare 404 with no body (Treasury accounts, invoicing signature,
  declaration files). The audit asked for the coded `Problems.NotFound` everywhere except the secret
  paths; that changes responses, so it is left for its own ticket.
