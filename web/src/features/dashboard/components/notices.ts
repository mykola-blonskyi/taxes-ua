import type { DashboardResponse } from "@/data/dashboard/useDashboard";

// The dashboard answers "what do I pay" first. The pay hero (or the state card that stands in for it) is
// always the first thing; the notices around it are ranked here so the screen can show at most one above
// it and fold the rest into a single "needs attention" list under it.
//
// The order is by consequence for the owner's money and legal standing, most to least:
//
//  1. Sync rejected or unreadable (TokenRejected, TokenUnreadable). Income stopped arriving, so every figure
//     below depends on it and the owner pays too little (ADR-026).
//  2. Limit crossing. The regime changes for the quarters it names.
//  3. Group 3 unconfirmed, or the application deadline. Missing the deadline cannot be undone (Rule 8).
//  4. Declaration due.
//  5. Sync stale. Figures may be incomplete, but the feed still works.
//  6. Transactions waiting for review.
//  7. Overdue invoices. A client's late payment has no tax consequence; it is a collections matter.
//
// A debt is the hero itself (red when overdue), so it needs no banner. Treasury account expiry is shown
// inside the hero's pay panel, where the account is used, so it never takes a banner slot here.
//
// A declaration or group 3 notice with URGENT_DAYS or fewer days left (or past) goes above all the others,
// since a deadline that close outranks every standing risk.
export const noticePriority = [
  "syncBroken",
  "limitCrossing",
  "group3",
  "declaration",
  "syncStale",
  "review",
  "overdueInvoices",
] as const;

export type Notice = (typeof noticePriority)[number];

export type Severity = "alert" | "warning" | "info";

// What the folded list's summary borrows its colour from: the most severe notice it holds.
export const noticeSeverity: Record<Notice, Severity> = {
  syncBroken: "alert",
  limitCrossing: "alert",
  group3: "warning",
  declaration: "warning",
  syncStale: "warning",
  review: "info",
  overdueInvoices: "info",
};

export const URGENT_DAYS = 3;

type Group3 = DashboardResponse["group3"];

// Registered, but not yet confirmed by the DPS register (ADR-023).
export function isGroup3Unconfirmed(group3: Group3): boolean {
  return !group3.confirmed && group3.group3Start !== null;
}

export function activeNotices(data: DashboardResponse): Notice[] {
  const syncState = data.sync?.state;
  const present: Record<Notice, boolean> = {
    syncBroken: syncState === "TokenRejected" || syncState === "TokenUnreadable",
    limitCrossing: data.limitCrossing != null,
    group3: isGroup3Unconfirmed(data.group3) || data.group3.beforeGroup3 != null,
    declaration: data.declaration != null,
    syncStale: syncState === "Stale",
    review: Number(data.needsReviewCount) > 0,
    overdueInvoices: Number(data.overdueInvoiceCount) > 0,
  };
  const urgent: Record<Notice, boolean> = {
    syncBroken: false,
    limitCrossing: false,
    group3:
      isGroup3Unconfirmed(data.group3) &&
      data.group3.applicationDaysLeft != null &&
      Number(data.group3.applicationDaysLeft) <= URGENT_DAYS,
    declaration: data.declaration != null && Number(data.declaration.daysLeft) <= URGENT_DAYS,
    syncStale: false,
    review: false,
    overdueInvoices: false,
  };
  const active = noticePriority.filter((notice) => present[notice]);

  return [...active.filter((notice) => urgent[notice]), ...active.filter((notice) => !urgent[notice])];
}

export function mostSevere(notices: readonly Notice[]): Severity {
  const severities = notices.map((notice) => noticeSeverity[notice]);

  return severities.includes("alert") ? "alert" : severities.includes("warning") ? "warning" : "info";
}
