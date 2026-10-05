"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";
import { periodsQueryKey } from "@/data/periods/usePeriods";
import { treasuryAccountsQueryKey } from "@/data/treasury/useTreasuryAccounts";

export type PaymentKind = components["schemas"]["PaymentKind"];
export type PaymentRequest = components["schemas"]["PaymentRequest"];
export type PaymentResponse = components["schemas"]["PaymentResponse"];

export type PaymentCandidate = components["schemas"]["PaymentCandidateResponse"];
export type PaymentMatch = components["schemas"]["PaymentMatchResponse"];
export type ConfirmCandidateRequest = components["schemas"]["ConfirmCandidateRequest"];

export const paymentKinds: PaymentKind[] = ["SingleTax", "MilitaryLevy", "Esv"];

export const paymentsQueryKey = ["payments"] as const;

export function usePayments(year: number | undefined) {
  return useQuery({
    queryKey: [...paymentsQueryKey, year],
    queryFn: async () => {
      const { data } = await api.GET("/api/payments", { params: { query: { year: year! } } });

      return data;
    },
    enabled: year !== undefined,
  });
}

// A payment moves the balances and the paid/remaining columns, which the periods response carries.
function useInvalidatePayments() {
  const queryClient = useQueryClient();

  return () => {
    queryClient.invalidateQueries({ queryKey: paymentsQueryKey });
    queryClient.invalidateQueries({ queryKey: periodsQueryKey });
    // A payment from a bank operation teaches its kind's Treasury account.
    queryClient.invalidateQueries({ queryKey: treasuryAccountsQueryKey });
  };
}

export function useCreatePayment() {
  const invalidate = useInvalidatePayments();

  return useMutation({
    mutationFn: async (body: PaymentRequest) => {
      const { data } = await api.POST("/api/payments", { body });

      return data;
    },
    onSuccess: invalidate,
  });
}

export function useUpdatePayment() {
  const invalidate = useInvalidatePayments();

  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: PaymentRequest }) => {
      const { data } = await api.PUT("/api/payments/{id}", { params: { path: { id } }, body });

      return data;
    },
    onSuccess: invalidate,
  });
}

export function useDeletePayment() {
  const invalidate = useInvalidatePayments();

  return useMutation({
    mutationFn: (id: string) => api.DELETE("/api/payments/{id}", { params: { path: { id } } }),
    onSuccess: invalidate,
  });
}

// Invalidates once, after the last payment, so a screen showing the debts does not refetch and change
// under a batch still being recorded. A payment that fails does not stop the rest: the answer names the
// kinds that were saved and, for each that was not, why, so the caller never re-sends a saved one.
export function useRecordPayments() {
  const invalidate = useInvalidatePayments();

  return useMutation({
    mutationFn: async (bodies: PaymentRequest[]) => {
      const saved: PaymentKind[] = [];
      const failed: { kind: PaymentKind; error: unknown }[] = [];

      for (const body of bodies) {
        try {
          await api.POST("/api/payments", { body });
          saved.push(body.kind);
        } catch (error) {
          failed.push({ kind: body.kind, error });
        }
      }

      return { saved, failed };
    },
    onSettled: invalidate,
  });
}

export function usePaymentCandidates() {
  return useQuery({
    queryKey: [...paymentsQueryKey, "candidates"],
    queryFn: async () => {
      const { data } = await api.GET("/api/payments/candidates");

      return data;
    },
  });
}

// Settled, not succeeded: a 409 means the list is stale, and reloading it is the recovery.
export function useConfirmCandidate() {
  const invalidate = useInvalidatePayments();

  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: ConfirmCandidateRequest }) => {
      const { data } = await api.POST("/api/payments/candidates/{id}/confirm", { params: { path: { id } }, body });

      return data;
    },
    onSettled: invalidate,
  });
}

export function useDismissCandidate() {
  const invalidate = useInvalidatePayments();

  return useMutation({
    mutationFn: (id: string) => api.POST("/api/payments/candidates/{id}/dismiss", { params: { path: { id } } }),
    onSettled: invalidate,
  });
}
