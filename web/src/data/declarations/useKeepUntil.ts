"use client";

import { useQuery } from "@tanstack/react-query";
import { api, ApiError } from "@/data/api/client";
import type { components } from "@/data/api/schema";
import { periodsQueryKey } from "@/data/periods/usePeriods";

export type YearKeepUntil = components["schemas"]["YearKeepUntilResponse"];
export type KeepUntil = components["schemas"]["KeepUntilResponse"];

// Nested under the periods key: the date follows the filed marks and the deadlines, so every write that
// refreshes the periods, filing or unfiling a declaration included, refreshes it too.
export const keepUntilQueryKey = [...periodsQueryKey, "keep-until"] as const;

export function useKeepUntil(year: number | undefined) {
  return useQuery({
    queryKey: [...keepUntilQueryKey, year],
    queryFn: async () => {
      const { data } = await api.GET("/api/declarations/{year}/keep-until", { params: { path: { year: year! } } });

      return data;
    },
    enabled: year !== undefined,
    retry: (failureCount, error) => !(error instanceof ApiError && error.status === 404) && failureCount < 1,
  });
}
