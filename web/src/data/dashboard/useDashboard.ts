"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";
import { periodsQueryKey } from "@/data/periods/usePeriods";

export type DashboardResponse = components["schemas"]["DashboardResponse"];
export type KindDebt = components["schemas"]["KindDebtResponse"];

// Nested under the periods key: the dashboard is computed from the same receipts, payments, settings
// and tax years, so every write that invalidates the periods also moves the next step.
export const dashboardQueryKey = [...periodsQueryKey, "dashboard"] as const;

export function useDashboard() {
  return useQuery({
    queryKey: dashboardQueryKey,
    queryFn: async () => {
      const { data } = await api.GET("/api/dashboard");

      return data;
    },
  });
}
