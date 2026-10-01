"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";
import { dashboardQueryKey } from "@/data/dashboard/useDashboard";
import { clientsQueryKey, transactionsQueryKey } from "@/data/transactions/useTransactions";

export type MonobankConnectionResponse = components["schemas"]["MonobankConnectionResponse"];
export type MonobankAccountResponse = components["schemas"]["MonobankAccountResponse"];
export type LastSyncResponse = components["schemas"]["LastSyncResponse"];
export type WebhookStatusResponse = components["schemas"]["WebhookStatusResponse"];

export const monobankQueryKey = ["monobank", "connection"] as const;

const isSyncing = (connection: MonobankConnectionResponse | undefined) =>
  connection?.accounts.some((account) => account.syncPending) ?? false;

export function useMonobankConnection() {
  const queryClient = useQueryClient();

  return useQuery({
    queryKey: monobankQueryKey,
    queryFn: async () => {
      const wasSyncing = isSyncing(queryClient.getQueryData<MonobankConnectionResponse>(monobankQueryKey));
      const { data } = await api.GET("/api/monobank/connection");
      if (wasSyncing && !isSyncing(data)) {
        queryClient.invalidateQueries({ queryKey: transactionsQueryKey });
        queryClient.invalidateQueries({ queryKey: clientsQueryKey });
      }

      return data;
    },
    // A 503 (not configured) is a steady state the settings section renders on its own, not a
    // transient failure worth retrying.
    retry: false,
    // The worker paces statement calls a minute apart, so a sync of several accounts takes minutes, and a
    // webhook registration waits for its own minute after a token replaced within the last one.
    refetchInterval: (query) =>
      isSyncing(query.state.data) || query.state.data?.webhook?.state === "Pending" ? 5_000 : false,
  });
}

export function useSyncMonobank() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      const { data } = await api.POST("/api/monobank/sync");

      return data;
    },
    onSuccess: (data) => {
      queryClient.setQueryData(monobankQueryKey, data);
    },
  });
}

export function useSaveMonobankToken() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (token: string) => {
      const { data } = await api.PUT("/api/monobank/connection", { body: { token } });

      return data;
    },
    onSuccess: (data) => {
      queryClient.setQueryData(monobankQueryKey, data);
    },
  });
}

export function useSaveFollowedMonobankAccounts() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (followedExternalIds: string[]) => {
      const { data } = await api.PUT("/api/monobank/accounts", { body: { followedExternalIds } });

      return data;
    },
    onSuccess: (data) => {
      queryClient.setQueryData(monobankQueryKey, data);
    },
  });
}

export function useDisconnectMonobank() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      await api.DELETE("/api/monobank/connection");
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: monobankQueryKey });
      // The home screen's jar offer depends on the connection.
      queryClient.invalidateQueries({ queryKey: dashboardQueryKey });
    },
  });
}
