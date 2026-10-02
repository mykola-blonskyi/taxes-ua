import { describe, expect, it } from "vitest";
import type { DashboardResponse } from "@/data/dashboard/useDashboard";
import { renderApp, reply, screen, stubFetch, within } from "@/test/harness";
import { DashboardScreen } from "./DashboardScreen";
import { activeNotices, noticePriority } from "./notices";

const debt = {
  kind: "SingleTax",
  fromYear: 2026,
  fromQuarter: 3,
  toYear: 2026,
  toQuarter: 3,
  amountKop: 123_456,
  dueDate: "2026-10-19",
  status: "Due",
  daysLeft: 17,
  advanceMonth: null,
} as const;

const everything = {
  today: "2026-10-02",
  nextStep: { state: "Pay", now: [debt], later: [] },
  credits: [],
  needsReviewCount: 3,
  overdueInvoiceCount: 2,
  group3: {
    group3Start: "2026-09-28",
    confirmed: false,
    applicationDeadline: "2026-10-08",
    applicationDaysLeft: 6,
    beforeGroup3: null,
  },
  sync: { state: "Stale", lastSyncedAt: "2026-09-28T00:05:00Z" },
  limitCrossing: { quarter: 3, year: 2026, switchFromQuarter: 4, switchFromYear: 2026, backOnGroup3From: null },
  declaration: { year: 2026, quarter: 3, dueDate: "2026-10-09", daysLeft: 7 },
} as const;

function hero() {
  const element = document.getElementById("next-step-kinds")?.closest("section");

  if (!element) {
    throw new Error("The pay hero is not on the page.");
  }

  return element;
}

function before(a: Node, b: Node) {
  return Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);
}

const locales = [
  {
    locale: "uk",
    overdue: /інвойс(и|ів|у)? прострочено/,
    review: /чекають? перевірки|чекає перевірки/,
    summary: "Потребує уваги (5)",
  },
  {
    locale: "ru",
    overdue: /инвойс(а|ов)? просрочен/,
    review: /ждут? проверки|ждёт проверки/,
    summary: "Требует внимания (5)",
  },
] as const;

describe.each(locales)("Dashboard notices in $locale", ({ locale, overdue, review, summary }) => {
  it("puts the pay hero first with one banner above it, and folds the rest under it", async () => {
    stubFetch({ "GET /api/dashboard": everything });
    renderApp(<DashboardScreen />, { locale });

    await screen.findByText(overdue);
    const card = hero();

    expect(before(screen.getByText(overdue), card)).toBe(true);
    for (const alert of screen.getAllByRole("alert")) {
      expect(before(card, alert)).toBe(true);
    }

    const folded = card.parentElement?.querySelector("details");
    expect(folded).not.toBeNull();
    expect(folded).not.toHaveAttribute("open");
    expect(before(card, folded!)).toBe(true);
    expect(within(folded!).getByText(summary)).toBeInTheDocument();
    expect(within(folded!).getByText(review)).toBeInTheDocument();
  });

  it("shows nothing above the hero and no fold when there is nothing to say", async () => {
    stubFetch({
      "GET /api/dashboard": {
        ...everything,
        needsReviewCount: 0,
        overdueInvoiceCount: 0,
        sync: null,
        limitCrossing: null,
        declaration: null,
        group3: { ...everything.group3, confirmed: true },
      },
    });
    renderApp(<DashboardScreen />, { locale });

    await screen.findByText(/17/);
    const card = hero();

    expect(screen.queryAllByRole("alert")).toHaveLength(0);
    expect(document.querySelector("details")).toBeNull();
    expect(card.previousElementSibling).toBeNull();
  });
});

describe("Dashboard notice priority", () => {
  const quiet = {
    ...everything,
    needsReviewCount: 0,
    overdueInvoiceCount: 0,
    sync: null,
    limitCrossing: null,
    declaration: null,
    group3: { ...everything.group3, confirmed: true },
  } as unknown as DashboardResponse;

  const withNotices = (patch: object) => ({ ...quiet, ...patch }) as unknown as DashboardResponse;

  it("ranks overdue items, then sync health, then group 3, then the rest", () => {
    expect(activeNotices(everything as unknown as DashboardResponse)).toEqual([
      "overdueInvoices",
      "sync",
      "group3",
      "limitCrossing",
      "declaration",
      "review",
    ]);
    expect(noticePriority).toEqual(["overdueInvoices", "sync", "group3", "limitCrossing", "declaration", "review"]);
  });

  it("lets a sync problem lead once nothing is overdue", () => {
    const data = withNotices({ sync: everything.sync, group3: everything.group3, needsReviewCount: 1 });

    expect(activeNotices(data)).toEqual(["sync", "group3", "review"]);
  });

  it("does not count a healthy sync or a confirmed group 3", () => {
    const data = withNotices({ sync: { state: "Healthy", lastSyncedAt: "2026-10-02T06:00:00Z" } });

    expect(activeNotices(data)).toEqual([]);
  });

  it("counts a stretch before group 3 even when the registration is confirmed", () => {
    const beforeGroup3 = { from: "2026-07-01", to: "2026-09-27", incomeKop: 1000 };
    const data = withNotices({ group3: { ...quiet.group3, beforeGroup3 } });

    expect(activeNotices(data)).toEqual(["group3"]);
  });
});

describe("DashboardScreen load failure", () => {
  it.each([
    { locale: "uk", text: /Не вдалося завантажити наступний крок\./, retry: "Спробувати ще раз" },
    { locale: "ru", text: /Не удалось загрузить следующий шаг\./, retry: "Повторить" },
  ] as const)("announces the failure in $locale and loads again on retry", async ({ locale, text, retry }) => {
    let calls = 0;
    stubFetch({
      "GET /api/dashboard": () => (++calls === 1 ? reply(500, { title: "Boom" }) : { ...everything, sync: null }),
    });
    const { user } = renderApp(<DashboardScreen />, { locale });

    expect(await screen.findByRole("alert")).toHaveTextContent(text);
    await user.click(screen.getByRole("button", { name: retry }));

    await screen.findByText(/17/);
    expect(calls).toBe(2);
    expect(screen.queryByText(text)).not.toBeInTheDocument();
  });
});
