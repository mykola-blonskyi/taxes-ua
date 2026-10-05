import { describe, expect, it } from "vitest";
import { renderApp, screen, stubFetch } from "@/test/harness";
import { DashboardScreen } from "./DashboardScreen";

const base = {
  today: "2026-10-02",
  nextStep: { state: "AllDone" },
  credits: [],
  needsReviewCount: 0,
  expiredTreasuryAccounts: [],
  overdueInvoiceCount: 0,
  group3: {
    group3Start: "2026-09-28",
    confirmed: false,
    applicationDeadline: "2026-10-08",
    applicationDaysLeft: 6,
    beforeGroup3: null,
  },
} as const;

function precedes(a: Node, b: Node) {
  return Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);
}

describe("DashboardScreen sync health", () => {
  it("lets a rejected token take the one banner slot above the pay card, ahead of the group 3 notice", async () => {
    stubFetch({
      "GET /api/dashboard": { ...base, sync: { state: "TokenRejected", lastSyncedAt: "2026-09-28T00:05:00Z" } },
    });
    renderApp(<DashboardScreen />);

    const sync = await screen.findByRole("heading", { name: "monobank відхилив токен" });
    const done = screen.getByRole("heading", { name: "Усе сплачено" });
    const group3 = screen.getByText(/Групу 3 ще не підтверджено/);

    expect(precedes(sync, done)).toBe(true);
    expect(precedes(done, group3)).toBe(true);
  });

  it("folds a stale feed under the pay card, behind the group 3 notice", async () => {
    stubFetch({
      "GET /api/dashboard": { ...base, sync: { state: "Stale", lastSyncedAt: "2026-09-28T00:05:00Z" } },
    });
    renderApp(<DashboardScreen />);

    const group3 = await screen.findByText(/Групу 3 ще не підтверджено/);
    const done = screen.getByRole("heading", { name: "Усе сплачено" });
    const sync = screen.getByRole("heading", { name: "Дані з банку застаріли" });

    expect(precedes(group3, done)).toBe(true);
    expect(precedes(done, sync)).toBe(true);
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

    expect(precedes(done, line)).toBe(true);
    expect(screen.queryByText(/Потребує уваги/)).not.toBeInTheDocument();
  });
});

describe("DashboardScreen state cards that point at settings", () => {
  it("sends a missing tax year to the tax-year tab, not the first one", async () => {
    stubFetch({
      "GET /api/dashboard": { ...base, nextStep: { state: "MissingTaxYear", missingTaxYear: 2027 }, sync: null },
    });
    renderApp(<DashboardScreen />);

    await screen.findByRole("heading", { name: "Баланс недоступний" });

    expect(screen.getByRole("link", { name: "Перейти до налаштувань" })).toHaveAttribute("href", "/settings?tab=taxYears");
  });

  it("sends an unset registration date to the first tab", async () => {
    stubFetch({
      "GET /api/dashboard": { ...base, nextStep: { state: "RegistrationDateNotSet" }, sync: null },
    });
    renderApp(<DashboardScreen />);

    await screen.findByRole("heading", { name: "Вкажіть дату реєстрації ФОП" });

    expect(screen.getByRole("link", { name: "Перейти до налаштувань" })).toHaveAttribute("href", "/settings");
  });
});
