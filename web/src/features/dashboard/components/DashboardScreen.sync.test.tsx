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
  it("takes the one banner slot above the pay card, ahead of the group 3 notice, which folds below", async () => {
    stubFetch({
      "GET /api/dashboard": { ...base, sync: { state: "Stale", lastSyncedAt: "2026-09-28T00:05:00Z" } },
    });
    renderApp(<DashboardScreen />);

    const sync = await screen.findByRole("heading", { name: "Дані з банку застаріли" });
    const done = screen.getByRole("heading", { name: "Усе сплачено" });
    const group3 = screen.getByText(/Групу 3 ще не підтверджено/);

    expect(sync.compareDocumentPosition(done) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(done.compareDocumentPosition(group3) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("shows nothing about the bank for an owner who follows no account", async () => {
    stubFetch({ "GET /api/dashboard": { ...base, sync: null } });
    renderApp(<DashboardScreen />);

    await screen.findByRole("heading", { name: "Усе сплачено" });

    expect(screen.queryByText(/Банк:/)).not.toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("keeps the healthy line quiet under the pay card", async () => {
    stubFetch({
      "GET /api/dashboard": { ...base, sync: { state: "Healthy", lastSyncedAt: "2026-10-02T06:00:00Z" } },
    });
    renderApp(<DashboardScreen />);

    const done = await screen.findByRole("heading", { name: "Усе сплачено" });
    const line = screen.getByText(/Банк: останній успішний обмін/);

    expect(done.compareDocumentPosition(line) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(screen.queryByText(/Потребує уваги/)).not.toBeInTheDocument();
  });
});
