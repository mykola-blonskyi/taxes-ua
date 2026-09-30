"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { api, ApiError } from "@/data/api/client";
import type { components } from "@/data/api/schema";

export type PaymentDetails = components["schemas"]["PaymentDetailsResponse"];

export type PaymentTarget = {
  kind: PaymentDetails["kind"];
  periodYear: number;
  periodQuarter: number | null;
  periodMonth: number | null;
};

export const paymentDetailsQueryKey = ["payment-details"] as const;

export function isPeriodNotComputed(error: unknown): boolean {
  return error instanceof ApiError && error.status === 409;
}

export function usePaymentDetails(target: PaymentTarget, amountKop: number | null, open: boolean) {
  const { kind, periodYear, periodQuarter, periodMonth } = target;

  return useQuery({
    queryKey: [...paymentDetailsQueryKey, kind, periodYear, periodQuarter, periodMonth, amountKop],
    queryFn: async () => {
      const { data } = await api.GET("/api/payment-details", {
        params: {
          query: {
            kind,
            periodYear,
            ...(periodQuarter !== null ? { periodQuarter } : { periodMonth: periodMonth! }),
            amountKop: amountKop!,
          },
        },
      });

      return data;
    },
    enabled: open && amountKop !== null && amountKop > 0,
    placeholderData: keepPreviousData,
    // The treasury account can change in settings between two openings of the panel.
    staleTime: 0,
    retry: (failures, error) => !isPeriodNotComputed(error) && failures < 1,
  });
}
