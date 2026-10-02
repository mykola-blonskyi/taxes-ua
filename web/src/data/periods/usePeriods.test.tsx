import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { periodsQueryKey, useIsOutsideGroup3, type PeriodsResponse } from "./usePeriods";

type Balances = NonNullable<PeriodsResponse["balances"]>;

function periods(balances: boolean, group3Quarters: PeriodsResponse["group3Quarters"]): PeriodsResponse {
  return { balances: balances ? ({} as Balances) : null, group3Quarters } as PeriodsResponse;
}

// The hook is rendered once on the server, where a query answers from what is already cached and
// starts no request, so the answer is the whole of what the hook decides.
function hint(year: number, quarter: number, cached: PeriodsResponse | undefined, cachedYear = year): boolean {
  const client = new QueryClient();
  if (cached) {
    client.setQueryData([...periodsQueryKey, cachedYear], cached);
  }

  function Probe() {
    return <>{String(useIsOutsideGroup3(year, quarter))}</>;
  }

  return (
    renderToStaticMarkup(
      <QueryClientProvider client={client}>
        <Probe />
      </QueryClientProvider>,
    ) === "true"
  );
}

describe("useIsOutsideGroup3", () => {
  it.each([
    ["a quarter listed in group 3", [1, 2, 3, 4], 3, false],
    ["the first quarter of a year that starts in group 3", [1, 2, 3, 4], 1, false],
    ["a quarter before the owner moved to group 3", [3, 4], 2, true],
    ["a quarter after the owner left group 3", [1, 2], 3, true],
    ["a year with no group 3 quarter at all", [], 1, true],
    ["a listed quarter sent as text", ["3", "4"], 3, false],
    ["an unlisted quarter when the list is text", ["3", "4"], 1, true],
  ])("with balances: %s", (_name, group3Quarters, quarter, outside) => {
    expect(hint(2026, quarter, periods(true, group3Quarters))).toBe(outside);
  });

  it.each([
    ["a year without balances, quarter listed", [1, 2, 3, 4], 2],
    ["a year without balances, quarter not listed", [3, 4], 1],
    ["a year without balances and no quarters", [], 1],
  ])("never warns for %s", (_name, group3Quarters, quarter) => {
    expect(hint(2026, quarter, periods(false, group3Quarters))).toBe(false);
  });

  it("does not warn before the year's periods have loaded", () => {
    expect(hint(2026, 1, undefined)).toBe(false);
  });

  it("answers for the year asked about, not for another year's periods", () => {
    expect(hint(2026, 1, periods(true, [3, 4]), 2025)).toBe(false);
    expect(hint(2026, 1, periods(true, [3, 4]), 2026)).toBe(true);
  });
});
