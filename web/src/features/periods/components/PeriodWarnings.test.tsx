import { describe, expect, it } from "vitest";
import type { PeriodsResponse } from "@/data/periods/usePeriods";
import { renderApp, screen, stubFetch } from "@/test/harness";
import { PeriodWarnings } from "./PeriodWarnings";

const none: PeriodsResponse["warnings"] = {
  taxYearUnverified: false,
  fopRegistrationDateNotSet: false,
  excludedOperationCount: 0,
  negativeCumulativeTaxQuarters: [],
  yearBeforeRegistration: false,
  missingTaxYear: null,
  beforeGroup3: null,
};

// Under a thousand hryvnias, so no grouping separator (which ICU spells differently) is involved.
const beforeGroup3 = { ...none, beforeGroup3: { from: "2026-09-28", to: "2026-09-30", incomeKop: 50_000 } };

describe("PeriodWarnings", () => {
  it("names the stretch before group 3 and the income in it, with a link to the DPS status", () => {
    stubFetch({});
    renderApp(<PeriodWarnings year={2026} warnings={beforeGroup3} limitCrossing={null} />);

    const item = screen.getByRole("listitem");
    expect(item).toHaveTextContent(/^З \d.+ по \d.+ ви на загальній системі оподаткування й отримали за цей час 500,00 ₴\./);
    expect(item).toHaveTextContent("ЄСВ застосунок рахує");
    expect(screen.getByRole("link", { name: "Статус у ДПС" })).toHaveAttribute("href", "/settings?tab=dps");
  });

  it("says it in Russian", () => {
    stubFetch({});
    renderApp(<PeriodWarnings year={2026} warnings={beforeGroup3} limitCrossing={null} />, { locale: "ru" });

    expect(screen.getByRole("listitem")).toHaveTextContent(/^С \d.+ по \d.+ вы на общей системе налогообложения и получили за это время 500,00 ₴\./);
    expect(screen.getByRole("link", { name: "Статус в ДПС" })).toBeVisible();
  });

  it("shows nothing without a stretch before group 3", () => {
    stubFetch({});
    const { container } = renderApp(<PeriodWarnings year={2026} warnings={none} limitCrossing={null} />);

    expect(container).toBeEmptyDOMElement();
  });
});
