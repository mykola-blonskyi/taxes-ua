import { beforeEach, describe, expect, it, vi } from "vitest";
import type { TaxYearConfigResponse } from "@/data/tax-years/useTaxYears";
import { renderApp, reply, screen, stubFetch, useFakeTimers, within } from "@/test/harness";
import { TaxYearTable } from "./TaxYearTable";

const list = "GET /api/tax-years" as const;
const save = "PUT /api/tax-years/{year}" as const;
const verify = "POST /api/tax-years/{year}/verify" as const;
const clone = "POST /api/tax-years/{year}/clone-to/{next}" as const;

const year2026: TaxYearConfigResponse = {
  year: 2026,
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
  holidays: ["2026-01-01", "2026-08-24"],
  source: "Закон про держбюджет",
  verifiedAt: null,
};

const verified2025: TaxYearConfigResponse = { ...year2026, year: 2025, source: "Минюст", verifiedAt: "2026-01-05T10:00:00Z" };

const copy = {
  uk: {
    minWage: "Мінімальна зарплата",
    singleTax: "Єдиний податок",
    militaryLevy: "Військовий збір",
    esvRate: "Ставка ЄСВ",
    excessRate: "Ставка понад ліміт",
    esvMonthly: "ЄСВ на місяць",
    limitWages: "Ліміт доходу (у мін. зарплатах)",
    limit: "Ліміт доходу",
    thresholds: "Пороги попередження, %",
    thresholdsHint: "Через кому, наприклад: 85, 100",
    esvDay: "Термін сплати ЄСВ (день місяця)",
    declarationDays: "Днів на подання декларації",
    paymentDays: "Днів на сплату після декларації",
    advanceDay: "Рекомендований день авансу",
    group3Days: "Днів на заяву про 3 групу",
    holidays: "Святкові дні",
    source: "Джерело",
    unverified: "Не перевірено",
    save: "Зберегти",
    verify: "Позначити перевіреним",
    clone: "Клонувати в 2027",
    history: "Історія",
  },
  ru: {
    minWage: "Минимальная зарплата",
    singleTax: "Единый налог",
    militaryLevy: "Военный сбор",
    esvRate: "Ставка ЕСВ",
    excessRate: "Ставка сверх лимита",
    esvMonthly: "ЕСВ в месяц",
    limitWages: "Лимит дохода (в мин. зарплатах)",
    limit: "Лимит дохода",
    thresholds: "Пороги предупреждения, %",
    thresholdsHint: "Через запятую, например: 85, 100",
    esvDay: "Срок уплаты ЕСВ (день месяца)",
    declarationDays: "Дней на подачу декларации",
    paymentDays: "Дней на уплату после декларации",
    advanceDay: "Рекомендуемый день аванса",
    group3Days: "Дней на заявление о 3 группе",
    holidays: "Праздничные дни",
    source: "Источник",
    unverified: "Не проверено",
    save: "Сохранить",
    verify: "Отметить проверенным",
    clone: "Клонировать в 2027",
    history: "История",
  },
} as const;

describe.each(["uk", "ru"] as const)("TaxYearTable in %s", (locale) => {
  const words = copy[locale];

  it("shows every parameter of a year with its label and value", async () => {
    stubFetch({ [list]: [year2026] });
    renderApp(<TaxYearTable />, { locale });

    expect(await screen.findByLabelText(`${words.minWage} 2026`)).toHaveValue(802_800);
    expect(screen.getByLabelText(`${words.singleTax} 2026`)).toHaveValue(500);
    expect(screen.getByLabelText(`${words.militaryLevy} 2026`)).toHaveValue(100);
    expect(screen.getByLabelText(`${words.esvRate} 2026`)).toHaveValue(2200);
    expect(screen.getByLabelText(`${words.excessRate} 2026`)).toHaveValue(1300);
    expect(screen.getByLabelText(`${words.esvMonthly} 2026`)).toBeDisabled();
    expect(screen.getByLabelText(`${words.limitWages} 2026`)).toHaveValue(1167);
    expect(screen.getByLabelText(`${words.limit} 2026`)).toBeDisabled();
    expect(screen.getByLabelText(`${words.thresholds} 2026`)).toHaveValue("85, 100");
    expect(screen.getByLabelText(`${words.esvDay} 2026`)).toHaveValue(20);
    expect(screen.getByLabelText(`${words.declarationDays} 2026`)).toHaveValue(40);
    expect(screen.getByLabelText(`${words.paymentDays} 2026`)).toHaveValue(10);
    expect(screen.getByLabelText(`${words.advanceDay} 2026`)).toHaveValue(15);
    expect(screen.getByLabelText(`${words.group3Days} 2026`)).toHaveValue(10);
    expect(screen.getByLabelText(`${words.holidays} 2026`)).toHaveValue("2026-01-01, 2026-08-24");
    expect(screen.getByLabelText(`${words.source} 2026`)).toHaveValue("Закон про держбюджет");
    // The wide table's header and the narrow card's field both carry the hint.
    expect(screen.getAllByText(words.thresholdsHint).length).toBeGreaterThan(0);
    expect(screen.getByText(words.unverified, { exact: false })).toBeVisible();
  });

  it("keeps each year's parameters apart", async () => {
    stubFetch({ [list]: [year2026, verified2025] });
    renderApp(<TaxYearTable />, { locale });

    expect(await screen.findByLabelText(`${words.source} 2026`)).toHaveValue("Закон про держбюджет");
    expect(screen.getByLabelText(`${words.source} 2025`)).toHaveValue("Минюст");
    expect(screen.getAllByRole("button", { name: words.save })).toHaveLength(2);
  });

  it("saves the edited year from the same fields", async () => {
    const api = stubFetch({ [list]: [year2026], [save]: year2026 });
    const { user } = renderApp(<TaxYearTable />, { locale });

    const rate = await screen.findByLabelText(`${words.singleTax} 2026`);
    await user.clear(rate);
    await user.type(rate, "300");
    await user.click(screen.getByRole("button", { name: words.save }));

    const [request] = api.requestsTo(save);
    expect(request.path).toBe("/api/tax-years/2026");
    expect(request.body).toMatchObject({
      singleTaxRateBp: 300,
      minWageKop: 802_800,
      limitWarnThresholdsPct: [85, 100],
      group3ApplicationDays: 10,
    });
  });

  it("copies a year to the next one and marks it verified", async () => {
    const api = stubFetch({ [list]: [year2026], [clone]: year2026, [verify]: year2026 });
    const { user } = renderApp(<TaxYearTable />, { locale });

    await user.click(await screen.findByRole("button", { name: words.clone }));
    await user.click(screen.getByRole("button", { name: words.verify }));

    expect(api.requestsTo(clone)[0].path).toBe("/api/tax-years/2026/clone-to/2027");
    expect(api.requestsTo(verify)[0].path).toBe("/api/tax-years/2026/verify");
  });

  it("links a year to its history", async () => {
    stubFetch({ [list]: [year2026] });
    renderApp(<TaxYearTable />, { locale });

    const link = await screen.findByRole("link", { name: words.history });
    expect(link).toHaveAttribute("href", "/history?entity=TaxYearConfig&id=2026");
  });
});

describe("TaxYearTable verified year", () => {
  it("does not offer to verify a year that is already verified", async () => {
    stubFetch({ [list]: [verified2025] });
    renderApp(<TaxYearTable />);

    const row = (await screen.findByLabelText("Джерело 2025")).closest("tr")!;
    expect(within(row).getByRole("button", { name: "Позначити перевіреним" })).toBeDisabled();
  });
});

const offer = {
  uk: {
    text: /Параметри 2027 року ще не задано\. Клонуйте 2026 рік/,
    clone: "Клонувати 2026 у 2027",
  },
  ru: {
    text: /Параметры 2027 года ещё не заданы\. Клонируйте 2026 год/,
    clone: "Клонировать 2026 в 2027",
  },
} as const;

describe.each(["uk", "ru"] as const)("TaxYearTable offer of the coming year in %s", (locale) => {
  const words = offer[locale];

  beforeEach(() => {
    useFakeTimers(["Date"]);
    vi.setSystemTime(new Date("2026-12-05T10:00:00Z"));
  });

  it("offers to clone this year into the next when the next has no parameters", async () => {
    const api = stubFetch({ [list]: [year2026], [clone]: year2026 });
    const { user } = renderApp(<TaxYearTable />, { locale });

    expect(await screen.findByText(words.text)).toBeVisible();
    await user.click(screen.getByRole("button", { name: words.clone }));

    expect(api.requestsTo(clone)[0].path).toBe("/api/tax-years/2026/clone-to/2027");
  });

  it("offers nothing once the next year has its row", async () => {
    stubFetch({ [list]: [year2026, { ...year2026, year: 2027 }] });
    renderApp(<TaxYearTable />, { locale });

    await screen.findByLabelText(/2027/, { selector: "#min-wage-2027" });
    expect(screen.queryByText(words.text)).not.toBeInTheDocument();
  });

  it("offers nothing before December while the current year is configured", async () => {
    vi.setSystemTime(new Date("2026-11-30T10:00:00Z"));
    stubFetch({ [list]: [year2026] });
    renderApp(<TaxYearTable />, { locale });

    await screen.findByLabelText(/2026/, { selector: "#min-wage-2026" });
    expect(screen.queryByText(words.text)).not.toBeInTheDocument();
  });

  it("reads December and the year in Kyiv, not in the browser's zone", async () => {
    vi.setSystemTime(new Date("2026-11-30T22:30:00Z"));
    stubFetch({ [list]: [year2026] });
    renderApp(<TaxYearTable />, { locale });

    expect(await screen.findByText(words.text)).toBeVisible();
  });

  it("on the first of January with the new year missing offers to clone the latest year into it", async () => {
    vi.setSystemTime(new Date("2026-12-31T22:30:00Z"));
    const api = stubFetch({ [list]: [year2026], [clone]: year2026 });
    const { user } = renderApp(<TaxYearTable />, { locale });

    await user.click(await screen.findByRole("button", { name: words.clone }));

    expect(api.requestsTo(clone)[0].path).toBe("/api/tax-years/2026/clone-to/2027");
  });

  it("on the first of January offers the gap year first when two years are missing", async () => {
    vi.setSystemTime(new Date("2028-01-01T10:00:00Z"));
    stubFetch({ [list]: [year2026] });
    renderApp(<TaxYearTable />, { locale });

    expect(await screen.findByText(words.text)).toBeVisible();
  });

  it("offers nothing when the latest year is already past next year", async () => {
    vi.setSystemTime(new Date("2026-12-05T10:00:00Z"));
    stubFetch({ [list]: [{ ...year2026, year: 2027 }, year2026] });
    renderApp(<TaxYearTable />, { locale });

    await screen.findByLabelText(/2027/, { selector: "#min-wage-2027" });
    expect(screen.queryByText(words.text)).not.toBeInTheDocument();
  });

  it("words a refused clone and keeps the offer", async () => {
    stubFetch({ [list]: [year2026], [clone]: reply(409, { code: "tax_year_already_exists", title: "Exists" }) });
    const { user } = renderApp(<TaxYearTable />, { locale });

    await user.click(await screen.findByRole("button", { name: words.clone }));

    await screen.findByText(/./, { selector: "p.text-destructive" });
    expect(screen.getByRole("button", { name: words.clone })).toBeEnabled();
  });
});
