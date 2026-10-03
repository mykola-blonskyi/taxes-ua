import { beforeEach, describe, expect, it, vi } from "vitest";
import type { TaxYearConfigResponse } from "@/data/tax-years/useTaxYears";
import { renderApp, screen, stubFetch, useFakeTimers } from "@/test/harness";
import { TaxYearVerificationWarning } from "./TaxYearVerificationWarning";

const year = (value: number, verifiedAt: string | null): TaxYearConfigResponse => ({
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

const titles = {
  uk: { warning: "Потребують перевірки параметри року", tab: "/settings?tab=taxYears" },
  ru: { warning: "Требуют проверки параметры года", tab: "/settings?tab=taxYears" },
} as const;

describe.each(["uk", "ru"] as const)("TaxYearVerificationWarning in %s", (locale) => {
  const words = titles[locale];

  beforeEach(() => {
    useFakeTimers(["Date"]);
    vi.setSystemTime(new Date("2026-12-05T10:00:00Z"));
  });

  it("still names an unverified year up to the current one and links to the tax years tab", async () => {
    stubFetch({ "GET /api/tax-years": [year(2025, null), year(2026, "2026-01-05T10:00:00Z"), year(2027, null)] });
    renderApp(<TaxYearVerificationWarning />, { locale });

    const heading = await screen.findByRole("heading", { name: words.warning });

    expect(heading.closest("section")).toHaveTextContent("2025");
    expect(heading.closest("section")).not.toHaveTextContent("2027");
    expect(heading.closest("section")?.querySelector("a")).toHaveAttribute("href", words.tab);
  });

  it("warns about a current year with no row, read in Kyiv time", async () => {
    vi.setSystemTime(new Date("2026-12-31T22:30:00Z"));
    stubFetch({ "GET /api/tax-years": [year(2026, "2026-01-05T10:00:00Z")] });
    renderApp(<TaxYearVerificationWarning />, { locale });

    const heading = await screen.findByRole("heading", { name: words.warning });

    expect(heading.closest("section")).toHaveTextContent("2027");
  });
});
