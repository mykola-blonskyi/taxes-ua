GitHub: #238
Status: ready-for-agent
Blocked by: none

# Keep the dashboard up when the year's income is negative

## Parent

#237

## What to build

High. `GET /api/dashboard` returns 500 when the last group 3 quarter's year-to-date income is negative: `DashboardEndpoints.cs:97-99` passes it to `LimitMonitor.Evaluate`, which throws on negative input (`LimitMonitor.cs:36`). A January refund of a December receipt with no new income yet produces it. In the same function, the warn threshold is compared against a percentage rounded to whole basis points, so 85% minus 1 kopeck of the 2026 limit already shows Warn and "remaining" jumps from 0.01 to about 1.51M UAH.

## Acceptance criteria

- [ ] A failing API test first: a year whose only group 3 transaction is a refund of the previous year's receipt; the dashboard answers 200.
- [ ] Negative income evaluates as no income used (level Ok, full remaining).
- [ ] Thresholds compare in kopecks; an engine test with the real 2026 limit at 85% minus 1 kopeck and exactly 85%.
- [ ] Rule 4 in `knowledge/business-rules.md` states both behaviours.

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-05-full-audit.md, Backend #1 and #3.
