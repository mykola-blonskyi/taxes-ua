"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";

export type MonobankConnectionResponse = components["schemas"]["MonobankConnectionResponse"];
export type MonobankAccountResponse = components["schemas"]["MonobankAccountResponse"];

export const monobankQueryKey = ["monobank", "connection"] as const;

export function useMonobankConnection() {
  return useQuery({
    queryKey: monobankQueryKey,
    queryFn: async () => {
      const { data } = await api.GET("/api/monobank/connection");

      return data;
    },
    // A 503 (not configured) is a steady state the settings section renders on its own, not a
    // transient failure worth retrying.
    retry: false,
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
    },
  });
}
