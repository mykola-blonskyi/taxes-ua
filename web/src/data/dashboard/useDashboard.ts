"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";
import { periodsQueryKey } from "@/data/periods/usePeriods";

export type DashboardResponse = components["schemas"]["DashboardResponse"];
export type KindDebt = components["schemas"]["KindDebtResponse"];
export type Reserve = NonNullable<DashboardResponse["reserve"]>;
export type LimitStatus = components["schemas"]["LimitStatusResponse"];

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

// Allocation settles the oldest debt first whatever period a payment names, so naming the oldest open
// quarter, or the advance's month, only keeps the record readable.
export function recordedPeriodOf(debt: KindDebt): {
  periodYear: number;
  periodQuarter: number | null;
  periodMonth: number | null;
} {
  const advance = debt.advanceMonth !== null;

  return {
    periodYear: Number(debt.fromYear),
    periodQuarter: advance ? null : Number(debt.fromQuarter),
    periodMonth: advance ? Number(debt.advanceMonth) : null,
  };
}
