"use client";

import { useEffect, useState } from "react";
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

const amountDebounceMs = 400;

// Waits for the owner to stop typing, so a keystroke does not cost a request.
function useDebounced<T>(value: T): T {
  const [settled, setSettled] = useState(value);

  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), amountDebounceMs);

    return () => clearTimeout(timer);
  }, [value]);

  return settled;
}

// Two reads of one endpoint. The recipient, the purpose and what is missing do not depend on the amount,
// so they are fetched once per period, even before an amount is typed. The amount only fills the QR, so it
// is fetched, settled, for the amount alone and merged over the first.
export function usePaymentDetails(target: PaymentTarget, amountKop: number | null, open: boolean) {
  const { kind, periodYear, periodQuarter, periodMonth } = target;
  const period = {
    kind,
    periodYear,
    ...(periodQuarter !== null ? { periodQuarter } : { periodMonth: periodMonth! }),
  };
  const base = useQuery({
    queryKey: [...paymentDetailsQueryKey, kind, periodYear, periodQuarter, periodMonth],
    queryFn: async () => {
      const { data } = await api.GET("/api/payment-details", { params: { query: period } });

      return data;
    },
    enabled: open,
    // The treasury account can change in settings between two openings of the panel.
    staleTime: 0,
    retry: retryUnlessNotComputed,
  });
  const settled = useDebounced(amountKop !== null && amountKop > 0 ? amountKop : null);
  const withAmount = useQuery({
    queryKey: [...paymentDetailsQueryKey, kind, periodYear, periodQuarter, periodMonth, settled],
    queryFn: async () => {
      const { data } = await api.GET("/api/payment-details", { params: { query: { ...period, amountKop: settled! } } });

      return data;
    },
    enabled: open && settled !== null && base.data?.recipient != null,
    placeholderData: keepPreviousData,
    staleTime: 0,
    retry: retryUnlessNotComputed,
  });

  const qr = withAmount.data;
  // A null qrContent alone does not say whether the amount's answer is still coming or came without a QR.
  const qrPending = amountKop !== null && amountKop > 0 && qr === undefined && !withAmount.isError;
  const data = base.data
    ? { ...base.data, amountKop: qr?.amountKop ?? null, qrContent: qr?.qrContent ?? null, qrPending }
    : undefined;

  return {
    data,
    error: base.error ?? withAmount.error,
    isError: base.isError || withAmount.isError,
    isFetching: base.isFetching || withAmount.isFetching,
  };
}

function retryUnlessNotComputed(failures: number, error: unknown) {
  return !isPeriodNotComputed(error) && failures < 1;
}
