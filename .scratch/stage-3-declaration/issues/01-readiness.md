# 01: Check readiness and mark the quarter's declaration filed

GitHub: #110
Status: ready-for-agent
Blocked by: none
Parent: #109

## What to build

A Declaration screen per quarter (reachable from the periods screen and the home screen near the deadline). It shows the filing deadline and the payment deadline, the declaration's figures as they will appear in the form (cumulative income, tax, previous period's tax, tax payable, military levy lines) computed by a pure builder from the year's accruals and parameters, and a readiness checklist: receipts of the period still waiting for review, pending budget payment candidates, the year's parameters verified, the registration date set, declaration details complete; unpaid obligations as a warning. Each unmet item links to where it is fixed. The owner marks the quarter's declaration filed (date and type: reporting, new reporting, clarifying) and can undo it; a later change to the quarter's receipts shows a warning that a clarifying declaration may be needed. Settings gains declaration details: tax office region and district codes ("Код ДПІ" in the Cabinet), KVED codes, address as in the register; name and RNOKPP come from the invoicing details when present.

## Acceptance criteria

- [ ] Builder table tests: each quarter, a year with refunds, a partial first year, the military levy previous-period line, the 2026 reference figures; rates from year parameters.
- [ ] Readiness items appear and clear as their causes are fixed (HTTP seam tests).
- [ ] Filed mark, undo, and the post-filing change warning; backup round trip; owner isolation.
- [ ] Declaration rule in business-rules.md; domain model updated.
- [ ] Screen proved in a real browser at 375 px in uk and ru.
