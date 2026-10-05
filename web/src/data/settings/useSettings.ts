"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";
import { paymentsQueryKey } from "@/data/payments/usePayments";
import { periodsQueryKey } from "@/data/periods/usePeriods";
import { transactionsQueryKey } from "@/data/transactions/useTransactions";

export type SettingsRequest = components["schemas"]["SettingsRequest"];
export type SettingsResponse = components["schemas"]["SettingsResponse"];

export const settingsQueryKey = ["settings"] as const;

// refetchOnWindowFocus "always" reads the settings again on every return to the tab, however fresh they
// are, for a reader that must see a change made on another device.
export function useSettings({ refetchOnWindowFocus }: { refetchOnWindowFocus?: "always" } = {}) {
  return useQuery({
    queryKey: settingsQueryKey,
    refetchOnWindowFocus,
    queryFn: async () => {
      const { data } = await api.GET("/api/settings");

      return data;
    },
  });
}

export function useSaveSettings() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (body: SettingsRequest) => {
      const { data } = await api.PUT("/api/settings", { body });

      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: settingsQueryKey });
      queryClient.invalidateQueries({ queryKey: periodsQueryKey });
      queryClient.invalidateQueries({ queryKey: transactionsQueryKey });
      queryClient.invalidateQueries({ queryKey: paymentsQueryKey });
    },
  });
}
