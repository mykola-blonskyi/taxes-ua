import { beforeEach, describe, expect, it, vi } from "vitest";
import { DashboardScreen } from "@/features/dashboard";
import { TaxYearVerificationWarning } from "@/features/settings";
import { renderApp, screen, stubFetch, useFakeTimers } from "@/test/harness";

// The home page puts the verification warning above the dashboard (page.tsx); both read the tax years.
const year = (value: number, verifiedAt: string | null) => ({
  year: value,
  minWageKop: 802_800,
  singleTaxRateBp: 500,
  militaryLevyRateBp: 100,
  esvRateBp: 2200,
  excessRateBp: 1300,
  esvMonthlyKop: 176_616,
  incomeLimitMinWages: 1167,
  incomeLimitKop: 936_927_600,
  limitWarnThresholdsPct: [85, 100],
  esvDeadlineDay: 20,
  declarationDays: 40,
  taxPaymentDaysAfterDeclaration: 10,
  advanceRecommendedDay: 15,
  group3ApplicationDays: 10,
  holidays: [],
  source: "test",
  verifiedAt,
});

const dashboard = {
  today: "2026-12-05",
  nextStep: { state: "AllDone", now: [], later: [] },
  credits: [],
  needsReviewCount: 0,
  expiredTreasuryAccounts: [],
  overdueInvoiceCount: 0,
  group3: { group3Start: null, confirmed: true, applicationDeadline: null, applicationDaysLeft: null, beforeGroup3: null },
  sync: null,
  limitCrossing: null,
  declaration: null,
} as const;

const titles = {
  uk: { warning: "Потребують перевірки параметри року", notice: "Новий податковий рік 2027" },
  ru: { warning: "Требуют проверки параметры года", notice: "Новый налоговый год 2027" },
} as const;

describe.each(["uk", "ru"] as const)("The home page banners in %s", (locale) => {
  const words = titles[locale];

  beforeEach(() => {
    useFakeTimers(["Date"]);
    vi.setSystemTime(new Date("2026-12-05T10:00:00Z"));
  });

  it("leaves a cloned, unverified next year to the December notice: exactly one banner", async () => {
    stubFetch({
      "GET /api/tax-years": [year(2026, "2026-01-05T10:00:00Z"), year(2027, null)],
      "GET /api/dashboard": { ...dashboard, newTaxYear: { year: 2027, state: "Unconfirmed" } },
    });
    renderApp(
      <>
        <TaxYearVerificationWarning />
        <DashboardScreen />
      </>,
      { locale },
    );

    expect(await screen.findByRole("heading", { name: words.notice })).toBeVisible();
    expect(screen.queryByRole("heading", { name: words.warning })).not.toBeInTheDocument();
    expect(screen.getAllByRole("heading", { level: 3, name: /2027/ })).toHaveLength(1);
  });
});
