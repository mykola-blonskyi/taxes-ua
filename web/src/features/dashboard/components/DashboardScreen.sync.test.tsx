import { describe, expect, it } from "vitest";
import { renderApp, screen, stubFetch } from "@/test/harness";
import { DashboardScreen } from "./DashboardScreen";

const base = {
  today: "2026-10-02",
  nextStep: { state: "AllDone" },
  credits: [],
  needsReviewCount: 0,
  overdueInvoiceCount: 0,
  group3: {
    group3Start: "2026-09-28",
    confirmed: false,
    applicationDeadline: "2026-10-08",
    applicationDaysLeft: 6,
    beforeGroup3: null,
  },
} as const;

describe("DashboardScreen sync health", () => {
  it("sits under the pay card and below the group 3 banner, not above it", async () => {
    stubFetch({
      "GET /api/dashboard": { ...base, sync: { state: "Stale", lastSyncedAt: "2026-09-28T00:05:00Z" } },
    });
    renderApp(<DashboardScreen />);

    const group3 = await screen.findByText(/Групу 3 ще не підтверджено/);
    const done = screen.getByRole("heading", { name: "Усе сплачено" });
    const sync = screen.getByRole("heading", { name: "Дані з банку застаріли" });

    expect(group3.compareDocumentPosition(sync) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(done.compareDocumentPosition(sync) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("shows nothing about the bank for an owner who follows no account", async () => {
    stubFetch({ "GET /api/dashboard": { ...base, sync: null } });
    renderApp(<DashboardScreen />);

    await screen.findByRole("heading", { name: "Усе сплачено" });

    expect(screen.queryByText(/Банк:/)).not.toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
