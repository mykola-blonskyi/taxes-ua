import type { DashboardResponse } from "@/data/dashboard/useDashboard";

// The dashboard answers "what do I pay" first. The pay hero (or the state card that stands in for it) is
// always the first thing; the notices around it are ranked here so the screen can show at most one above
// it and fold the rest into a single "needs attention" list under it.
//
// The order is deliberate, most to least urgent for the owner's money and legal standing:
//
//  1. Debts and overdue items. A debt is the hero itself, overdue or not (it turns red there), so it needs
//     no banner. The overdue invoices notice is the item that does.
//  2. Sync health. A stalled or rejected bank feed means every figure below is short of the truth (ADR-026).
//  3. Group 3 status. An unconfirmed registration, or a stretch before it, makes the figures provisional
//     (ADR-023).
//  4. Treasury account expiry. It is shown inside the hero's pay panel, where the account is used, so it
//     never takes a banner slot here; it is listed to keep the ranking complete.
//  5. The others: the limit crossing, the declaration due and the transactions waiting for review.
export const noticePriority = [
  "overdueInvoices",
  "sync",
  "group3",
  "limitCrossing",
  "declaration",
  "review",
] as const;

export type Notice = (typeof noticePriority)[number];

export function activeNotices(data: DashboardResponse): Notice[] {
  const present: Record<Notice, boolean> = {
    overdueInvoices: Number(data.overdueInvoiceCount) > 0,
    sync: data.sync != null && data.sync.state !== "Healthy",
    group3: (!data.group3.confirmed && data.group3.group3Start !== null) || data.group3.beforeGroup3 != null,
    limitCrossing: data.limitCrossing != null,
    declaration: data.declaration != null,
    review: Number(data.needsReviewCount) > 0,
  };

  return noticePriority.filter((notice) => present[notice]);
}
