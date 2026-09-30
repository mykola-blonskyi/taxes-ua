"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";
import { clientsQueryKey, transactionsQueryKey } from "@/data/transactions/useTransactions";

export type ClientRequest = components["schemas"]["ClientRequest"];
export type ClientResponse = components["schemas"]["ClientResponse"];

export function useClients() {
  return useQuery({
    queryKey: clientsQueryKey,
    queryFn: async () => {
      const { data } = await api.GET("/api/clients");

      return data;
    },
  });
}

function useInvalidateClients() {
  const queryClient = useQueryClient();

  return () => {
    queryClient.invalidateQueries({ queryKey: clientsQueryKey });
    queryClient.invalidateQueries({ queryKey: transactionsQueryKey });
  };
}

export function useCreateClient() {
  const invalidate = useInvalidateClients();

  return useMutation({
    mutationFn: async (body: ClientRequest) => {
      const { data } = await api.POST("/api/clients", { body });

      return data;
    },
    onSuccess: invalidate,
  });
}

export function useUpdateClient() {
  const invalidate = useInvalidateClients();

  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: ClientRequest }) => {
      const { data } = await api.PUT("/api/clients/{id}", { params: { path: { id } }, body });

      return data;
    },
    onSuccess: invalidate,
  });
}

export function useDeleteClient() {
  const invalidate = useInvalidateClients();

  return useMutation({
    mutationFn: async (id: string) => {
      await api.DELETE("/api/clients/{id}", { params: { path: { id } } });
    },
    onSuccess: invalidate,
    // A 409 means a receipt now points at the client, so the list's receipt count is stale.
    onError: invalidate,
  });
}
