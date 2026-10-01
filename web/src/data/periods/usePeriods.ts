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

// A payment naming a quarter outside group 3 is listed apart only when the year has balances; outside
// the ledger nothing is, so there is nothing to warn about.
export function useIsOutsideGroup3(year: number, quarter: number): boolean {
  const periods = usePeriods(year);
  if (!periods.data?.balances) {
    return false;
  }

  return !periods.data.group3Quarters.some((inGroup3) => Number(inGroup3) === quarter);
}
