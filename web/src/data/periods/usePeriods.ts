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
