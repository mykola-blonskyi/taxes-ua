"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";

export type TreasuryAccount = components["schemas"]["TreasuryAccountResponse"];
export type TreasuryAccountLearned = components["schemas"]["TreasuryAccountLearned"];
export type TreasuryAccountRequest = components["schemas"]["TreasuryAccountRequest"];
export type TreasuryKind = TreasuryAccount["kind"];

export const treasuryAccountsQueryKey = ["settings", "treasury-accounts"] as const;

export function useTreasuryAccounts() {
  return useQuery({
    queryKey: treasuryAccountsQueryKey,
    queryFn: async () => {
      const { data } = await api.GET("/api/settings/treasury-accounts");

      return data;
    },
  });
}

export function useSaveTreasuryAccount() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ kind, body }: { kind: TreasuryKind; body: TreasuryAccountRequest }) => {
      const { data } = await api.PUT("/api/settings/treasury-accounts/{kind}", { params: { path: { kind } }, body });

      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: treasuryAccountsQueryKey }),
  });
}

export function useSetTreasuryValidUntil() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ kind, validUntil }: { kind: TreasuryKind; validUntil: string | null }) => {
      const { data } = await api.PUT("/api/settings/treasury-accounts/{kind}/valid-until", {
        params: { path: { kind } },
        body: { validUntil },
      });

      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: treasuryAccountsQueryKey }),
  });
}

export function useRevertTreasuryAccount() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (kind: TreasuryKind) => {
      const { data } = await api.POST("/api/settings/treasury-accounts/{kind}/revert", { params: { path: { kind } } });

      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: treasuryAccountsQueryKey }),
  });
}

export function useDismissTreasuryNotice() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (kind: TreasuryKind) => {
      await api.POST("/api/settings/treasury-accounts/{kind}/notice/dismiss", { params: { path: { kind } } });
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: treasuryAccountsQueryKey }),
  });
}
