import { describe, expect, it } from "vitest";
import { renderApp, reply, screen, stubFetch, waitFor } from "@/test/harness";
import { DashboardScreen } from "./DashboardScreen";

const finished = {
  today: "2026-02-12",
  nextStep: { state: "AllDone", now: [], later: [] },
  credits: [],
  needsReviewCount: 0,
  overdueInvoiceCount: 0,
  expiredTreasuryAccounts: [],
  group3: { group3Start: "2025-01-01", confirmed: true, applicationDeadline: null, applicationDaysLeft: null, beforeGroup3: null },
  sync: null,
  limitCrossing: null,
  declaration: null,
  newTaxYear: null,
  archive: { year: 2025, keepUntil: { state: "ExtendedWhileSuspended", date: "2029-02-10", daysAfterSuspension: 1095 } },
} as const;

describe("The dashboard's archive notice", () => {
  it("names the finished year, says until when to keep it as the periods screen does, and links to its archive", async () => {
    stubFetch({ "GET /api/dashboard": finished });
    renderApp(<DashboardScreen />);

    const notice = (await screen.findByRole("heading", { name: "Архів 2025 року готовий" })).closest("section")!;

    expect(notice).toHaveTextContent("Усі декларації за 2025 рік подано.");
    expect(notice).toHaveTextContent(/Документи за 2025 рік зберігати не менше ніж до .*2029.* ще 1\s?095 днів\./);
    expect(screen.getByRole("link", { name: "Відкрити архів" })).toHaveAttribute("href", "/archive?year=2025");
  });

  it("reads a fixed date in Russian", async () => {
    stubFetch({
      "GET /api/dashboard": { ...finished, archive: { year: 2015, keepUntil: { state: "Fixed", date: "2019-01-31", daysAfterSuspension: null } } },
    });
    renderApp(<DashboardScreen />, { locale: "ru" });

    const notice = (await screen.findByRole("heading", { name: "Архив 2015 года готов" })).closest("section")!;

    expect(notice).toHaveTextContent(/Документы за 2015 год хранить до .*2019/);
    expect(screen.getByRole("link", { name: "Открыть архив" })).toHaveAttribute("href", "/archive?year=2015");
    expect(screen.getByRole("button", { name: "Скрыть" })).toBeVisible();
  });

  it("goes away once dismissed", async () => {
    let dismissed = false;
    const api = stubFetch({
      "GET /api/dashboard": () => (dismissed ? { ...finished, archive: null } : finished),
      "POST /api/archive/{year}/notice/dismiss": () => {
        dismissed = true;
        return reply(204);
      },
    });
    const { user } = renderApp(<DashboardScreen />);

    await user.click(await screen.findByRole("button", { name: "Сховати" }));

    await waitFor(() => expect(screen.queryByRole("heading", { name: "Архів 2025 року готовий" })).toBeNull());
    expect(api.requestsTo("POST /api/archive/{year}/notice/dismiss").map((request) => request.path)).toEqual([
      "/api/archive/2025/notice/dismiss",
    ]);
  });

  it("stays and says so when the dismissal fails", async () => {
    stubFetch({
      "GET /api/dashboard": finished,
      "POST /api/archive/{year}/notice/dismiss": reply(500),
    });
    const { user } = renderApp(<DashboardScreen />);

    await user.click(await screen.findByRole("button", { name: "Сховати" }));

    expect(await screen.findByText(/Не вдалося сховати повідомлення\./)).toBeVisible();
    expect(screen.getByRole("heading", { name: "Архів 2025 року готовий" })).toBeVisible();
  });

  it("folds under the hero with a quiet summary when another notice takes the banner", async () => {
    stubFetch({ "GET /api/dashboard": { ...finished, needsReviewCount: 2 } });
    renderApp(<DashboardScreen />);

    const folded = await waitFor(() => {
      const details = document.querySelector("details");
      expect(details).not.toBeNull();
      return details!;
    });

    expect(folded).toHaveAttribute("data-severity", "info");
    expect(folded.querySelector("summary")).toHaveTextContent("Архів 2025 року готовий");
  });
});
