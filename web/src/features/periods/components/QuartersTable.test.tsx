import { describe, expect, it } from "vitest";
import type { PeriodsResponse } from "@/data/periods/usePeriods";
import { renderApp, screen, stubFetch } from "@/test/harness";
import { QuartersTable } from "./QuartersTable";

type Quarter = PeriodsResponse["quarters"][number];

const on = (date: string) => ({ statutory: date, due: date });

function quarter(number: number, group3: boolean, deadlines: [string, string, string]): Quarter {
  return {
    quarter: number,
    incomeKop: 0,
    singleTaxKop: 0,
    militaryLevyKop: 0,
    esvKop: 190_234,
    totalKop: 190_234,
    cumulativeIncomeKop: 0,
    cumulativeSingleTaxKop: 0,
    cumulativeExcessIncomeKop: 0,
    cumulativeExcessTaxKop: 0,
    cumulativeMilitaryLevyKop: 0,
    deadlines: { esv: on(deadlines[0]), declaration: on(deadlines[1]), taxPayment: on(deadlines[2]) },
    obligations: null,
    group3,
  };
}

// Registered on 28 September with group 3 from 1 October: Q3 owes ESV only.
const quarters = [
  quarter(3, false, ["2026-10-19", "2026-11-09", "2026-11-19"]),
  quarter(4, true, ["2027-01-19", "2027-02-09", "2027-02-19"]),
];

describe("QuartersTable", () => {
  it("shows a quarter before group 3 with its ESV date and no declaration or single tax date", () => {
    stubFetch({});
    renderApp(<QuartersTable quarters={quarters} />);

    expect(screen.getAllByText("19.10.2026").length).toBeGreaterThan(0);
    expect(screen.queryByText("09.11.2026")).not.toBeInTheDocument();
    expect(screen.queryByText("19.11.2026")).not.toBeInTheDocument();
    expect(screen.getAllByText("09.02.2027").length).toBeGreaterThan(0);
    expect(screen.getAllByText("19.02.2027").length).toBeGreaterThan(0);
  });
});
