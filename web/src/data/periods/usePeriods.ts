"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";

export type PeriodsResponse = components["schemas"]["PeriodsResponse"];

export const periodsQueryKey = ["periods"] as const;

export function usePeriods(year: number | undefined) {
  return useQuery({
    queryKey: [...periodsQueryKey, year],
    queryFn: async () => {
      const { data } = await api.GET("/api/periods/{year}", { params: { path: { year: year! } } });

      return data;
    },
    enabled: year !== undefined,
  });
}

// Mirrors the engine (Rule 4): after the switch the crossing names, a quarter the response leaves out is
// outside group 3, and a payment naming it stays out of the ledger.
export function useIsOutsideGroup3(year: number, quarter: number): boolean {
  const periods = usePeriods(year);
  const crossing = periods.data?.limitCrossing;
  if (!periods.data || !crossing) {
    return false;
  }

  const switchYear = Number(crossing.switchFromYear);
  const afterSwitch = year > switchYear || (year === switchYear && quarter >= Number(crossing.switchFromQuarter));

  return afterSwitch && !periods.data.quarters.some((accrual) => Number(accrual.quarter) === quarter);
}
