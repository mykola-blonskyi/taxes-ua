import { describe, expect, it } from "vitest";
import type { DashboardResponse } from "@/data/dashboard/useDashboard";
import { renderApp, reply, screen, stubFetch, within } from "@/test/harness";
import { DashboardScreen } from "./DashboardScreen";
import { activeNotices, mostSevere, noticePriority } from "./notices";

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
  sync: { state: "TokenRejected", lastSyncedAt: "2026-09-28T00:05:00Z" },
  limitCrossing: { quarter: 3, year: 2026, switchFromQuarter: 4, switchFromYear: 2026, backOnGroup3From: null },
  declaration: { year: 2026, quarter: 3, dueDate: "2026-10-09", daysLeft: 7 },
} as const;

const quiet = {
  ...everything,
  needsReviewCount: 0,
  overdueInvoiceCount: 0,
  sync: null,
  limitCrossing: null,
  declaration: null,
  group3: { ...everything.group3, confirmed: true },
};

const asData = (value: object) => value as unknown as DashboardResponse;

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
    banner: "monobank відхилив токен",
    overdue: /інвойс(и|ів|у)? прострочено/,
    summary: "Потребує уваги (5)",
  },
  {
    locale: "ru",
    banner: "monobank отклонил токен",
    overdue: /инвойс(а|ов)? просрочен/,
    summary: "Требует внимания (5)",
  },
] as const;

describe.each(locales)("Dashboard notices in $locale", ({ locale, banner, overdue, summary }) => {
  it("puts the pay hero first with one banner above it, and folds the rest under it", async () => {
    stubFetch({ "GET /api/dashboard": everything });
    renderApp(<DashboardScreen />, { locale });

    const bannerTitle = await screen.findByRole("heading", { name: banner });
    const card = hero();

    expect(before(bannerTitle, card)).toBe(true);
    for (const alert of screen.getAllByRole("alert")) {
      expect(alert === bannerTitle.closest("section") || before(card, alert)).toBe(true);
    }

    const folded = document.querySelector("details");
    expect(folded).not.toBeNull();
    expect(folded).not.toHaveAttribute("open");
    expect(before(card, folded!)).toBe(true);
    expect(folded!.querySelector("summary")).toHaveTextContent(summary);
  });

  it("names the folded notices in the summary and tints it red while one of them is an alert", async () => {
    stubFetch({ "GET /api/dashboard": everything });
    renderApp(<DashboardScreen />, { locale });

    await screen.findByRole("heading", { name: banner });
    const folded = document.querySelector("details")!;

    expect(folded).toHaveAttribute("data-severity", "alert");
    expect(folded.querySelector("summary")).toHaveTextContent(overdue);
    expect(folded.querySelector("summary")?.className).toContain("text-destructive");
  });

  it("shows nothing above the hero and no fold when there is nothing to say", async () => {
    stubFetch({ "GET /api/dashboard": quiet });
    renderApp(<DashboardScreen />, { locale });

    await screen.findByText(/17/);
    const card = hero();

    expect(screen.queryAllByRole("alert")).toHaveLength(0);
    expect(document.querySelector("details")).toBeNull();
    expect(card.previousElementSibling).toBeNull();
  });
});

describe("Dashboard notice priority", () => {
  const withNotices = (patch: object) => asData({ ...quiet, ...patch });

  it("ranks by consequence: broken sync, limit crossing, group 3, declaration, new tax year, stale sync, review, overdue invoices", () => {
    const stale = { ...everything, sync: { state: "Stale", lastSyncedAt: "2026-09-28T00:05:00Z" } };

    expect(activeNotices(asData(everything))).toEqual([
      "syncBroken",
      "limitCrossing",
      "group3",
      "declaration",
      "review",
      "overdueInvoices",
    ]);
    expect(activeNotices(asData(stale))).toEqual([
      "limitCrossing",
      "group3",
      "declaration",
      "syncStale",
      "review",
      "overdueInvoices",
    ]);
    expect(noticePriority).toEqual([
      "syncBroken",
      "limitCrossing",
      "group3",
      "declaration",
      "newTaxYear",
      "syncStale",
      "review",
      "overdueInvoices",
    ]);
  });

  it("ranks the new tax year under a declaration and over a stale sync, and never promotes it", () => {
    const newTaxYear = { year: 2027, state: "Missing" };
    const stale = { ...everything, sync: { state: "Stale", lastSyncedAt: "2026-09-28T00:05:00Z" }, newTaxYear };

    expect(activeNotices(asData(stale))).toEqual([
      "limitCrossing",
      "group3",
      "declaration",
      "newTaxYear",
      "syncStale",
      "review",
      "overdueInvoices",
    ]);
    expect(activeNotices(withNotices({ newTaxYear, declaration: everything.declaration }))).toEqual([
      "declaration",
      "newTaxYear",
    ]);
    expect(activeNotices(withNotices({ newTaxYear, needsReviewCount: 2 }))).toEqual(["newTaxYear", "review"]);
    expect(mostSevere(["newTaxYear", "review"])).toBe("warning");
  });

  it("puts an unreadable token with the rejected one, and a client's overdue invoice last", () => {
    const data = withNotices({ sync: { state: "TokenUnreadable", lastSyncedAt: null }, overdueInvoiceCount: 4 });

    expect(activeNotices(data)).toEqual(["syncBroken", "overdueInvoices"]);
  });

  it("does not count a healthy sync or a confirmed group 3", () => {
    const data = withNotices({ sync: { state: "Healthy", lastSyncedAt: "2026-10-02T06:00:00Z" } });

    expect(activeNotices(data)).toEqual([]);
  });

  it("counts a stretch before group 3 even when the registration is confirmed", () => {
    const beforeGroup3 = { from: "2026-07-01", to: "2026-09-27", incomeKop: 1000 };

    expect(activeNotices(withNotices({ group3: { ...quiet.group3, beforeGroup3 } }))).toEqual(["group3"]);
  });

  it("promotes a declaration with three days left or fewer above every other notice", () => {
    const data = asData({ ...everything, declaration: { ...everything.declaration, daysLeft: 3 } });

    expect(activeNotices(data)[0]).toBe("declaration");
    expect(activeNotices(data).slice(1)).toEqual(["syncBroken", "limitCrossing", "group3", "review", "overdueInvoices"]);
  });

  it("does not promote a declaration with four days left", () => {
    const data = asData({ ...everything, declaration: { ...everything.declaration, daysLeft: 4 } });

    expect(activeNotices(data)[0]).toBe("syncBroken");
  });

  it("promotes an application deadline that is three days away, or already past", () => {
    for (const applicationDaysLeft of [3, 0, -2]) {
      const data = asData({ ...everything, group3: { ...everything.group3, applicationDaysLeft } });

      expect(activeNotices(data)[0]).toBe("group3");
    }
  });

  it("keeps the usual order between two urgent notices", () => {
    const data = asData({
      ...everything,
      group3: { ...everything.group3, applicationDaysLeft: 1 },
      declaration: { ...everything.declaration, daysLeft: 1 },
    });

    expect(activeNotices(data).slice(0, 3)).toEqual(["group3", "declaration", "syncBroken"]);
  });

  it("does not promote a group 3 deadline once the registration is confirmed", () => {
    const data = asData({ ...everything, group3: { ...everything.group3, confirmed: true, applicationDaysLeft: 1 } });

    expect(activeNotices(data)).not.toContain("group3");
  });

  it("takes the most severe tone of what it holds", () => {
    expect(mostSevere(["review", "overdueInvoices"])).toBe("info");
    expect(mostSevere(["review", "declaration"])).toBe("warning");
    expect(mostSevere(["declaration", "syncBroken", "review"])).toBe("alert");
    expect(mostSevere(["limitCrossing"])).toBe("alert");
  });
});

describe("Dashboard folded summary tone", () => {
  it("is amber when the folded notices are warnings, not alerts", async () => {
    stubFetch({ "GET /api/dashboard": { ...quiet, declaration: everything.declaration, group3: everything.group3 } });
    renderApp(<DashboardScreen />);

    await screen.findByText(/17/);
    const folded = document.querySelector("details")!;

    expect(folded).toHaveAttribute("data-severity", "warning");
    expect(folded.querySelector("summary")?.className).toContain("amber");
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

const newYear = {
  uk: {
    title: "Новий податковий рік 2027",
    missing: /Параметри 2027 року ще не задано\./,
    unconfirmed: /Параметри 2027 року ще не підтверджено\./,
    cta: "Відкрити податкові роки",
  },
  ru: {
    title: "Новый налоговый год 2027",
    missing: /Параметры 2027 года ещё не заданы\./,
    unconfirmed: /Параметры 2027 года ещё не подтверждены\./,
    cta: "Открыть налоговые годы",
  },
} as const;

describe.each(["uk", "ru"] as const)("Dashboard new tax year notice in %s", (locale) => {
  const words = newYear[locale];

  it("is the banner above the hero when nothing outranks it, and opens the tax years tab", async () => {
    stubFetch({ "GET /api/dashboard": { ...quiet, newTaxYear: { year: 2027, state: "Missing" } } });
    renderApp(<DashboardScreen />, { locale });

    const title = await screen.findByRole("heading", { name: words.title });
    const notice = title.closest("section")!;

    expect(before(title, hero())).toBe(true);
    expect(notice).toHaveTextContent(words.missing);
    expect(within(notice).getByRole("link", { name: words.cta })).toHaveAttribute("href", "/settings?tab=taxYears");
    expect(document.querySelector("details")).toBeNull();
  });

  it("says the year is unconfirmed when a copy exists", async () => {
    stubFetch({ "GET /api/dashboard": { ...quiet, newTaxYear: { year: 2027, state: "Unconfirmed" } } });
    renderApp(<DashboardScreen />, { locale });

    const title = await screen.findByRole("heading", { name: words.title });

    expect(title.closest("section")).toHaveTextContent(words.unconfirmed);
  });

  it("folds under the hero and names itself in the summary when a declaration is the banner", async () => {
    stubFetch({
      "GET /api/dashboard": { ...quiet, declaration: everything.declaration, newTaxYear: { year: 2027, state: "Missing" } },
    });
    renderApp(<DashboardScreen />, { locale });

    await screen.findByText(/17/);
    const folded = document.querySelector("details")!;

    expect(folded.querySelector("summary")).toHaveTextContent(words.title);
    expect(before(hero(), folded)).toBe(true);
  });
});
