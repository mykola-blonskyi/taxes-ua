"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";

export type Currency = components["schemas"]["Currency"];
export type FxRateResponse = components["schemas"]["FxRateResponse"];

export const currencies: readonly Currency[] = ["UAH", "USD", "EUR"];

const isoDatePattern = /^\d{4}-\d{2}-\d{2}$/;

export function useFxRate(currency: Currency, date: string, enabled: boolean) {
  return useQuery({
    queryKey: ["fx", currency, date],
    queryFn: async () => {
      const { data } = await api.GET("/api/fx", { params: { query: { currency, date } } });

      return data;
    },
    enabled: enabled && currency !== "UAH" && isoDatePattern.test(date),
    // A 502 means NBU is down; the form must offer the manual rate at once, not after retries.
    retry: false,
    staleTime: Infinity,
  });
}
