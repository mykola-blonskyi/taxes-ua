"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";

export type TransactionKind = components["schemas"]["TransactionKind"];
export type TransactionRequest = components["schemas"]["TransactionRequest"];
export type TransactionResponse = components["schemas"]["TransactionResponse"];
export type TransactionListResponse = components["schemas"]["TransactionListResponse"];
export type RefundedReceipt = components["schemas"]["RefundedReceipt"];
export type ReceiptOption = components["schemas"]["ReceiptOption"];

export const transactionsQueryKey = ["transactions"] as const;
export const clientsQueryKey = ["clients"] as const;

export function useTransactions(year: number) {
  return useQuery({
    queryKey: [...transactionsQueryKey, year],
    queryFn: async () => {
      const { data } = await api.GET("/api/transactions", { params: { query: { year } } });

      return data;
    },
  });
}

function useInvalidateTransactions() {
  const queryClient = useQueryClient();

  return () => {
    queryClient.invalidateQueries({ queryKey: transactionsQueryKey });
    queryClient.invalidateQueries({ queryKey: clientsQueryKey });
  };
}

export function useCreateTransaction() {
  const invalidate = useInvalidateTransactions();

  return useMutation({
    mutationFn: async (body: TransactionRequest) => {
      const { data } = await api.POST("/api/transactions", { body });

      return data;
    },
    onSuccess: invalidate,
  });
}

export function useUpdateTransaction() {
  const invalidate = useInvalidateTransactions();

  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: TransactionRequest }) => {
      const { data } = await api.PUT("/api/transactions/{id}", {
        params: { path: { id } },
        body,
      });

      return data;
    },
    onSuccess: invalidate,
  });
}

export function useDeleteTransaction() {
  const invalidate = useInvalidateTransactions();

  return useMutation({
    mutationFn: (id: string) => api.DELETE("/api/transactions/{id}", { params: { path: { id } } }),
    onSuccess: invalidate,
  });
}

export function useReceipts() {
  return useQuery({
    queryKey: [...transactionsQueryKey, "receipts"],
    queryFn: async () => {
      const { data } = await api.GET("/api/transactions/receipts");

      return data;
    },
  });
}

export function useClients() {
  return useQuery({
    queryKey: clientsQueryKey,
    queryFn: async () => {
      const { data } = await api.GET("/api/clients");

      return data;
    },
  });
}
