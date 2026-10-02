"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";
import { paymentsQueryKey } from "@/data/payments/usePayments";
import { periodsQueryKey } from "@/data/periods/usePeriods";
import { transactionsQueryKey } from "@/data/transactions/useTransactions";

export type DpsStatusRequest = components["schemas"]["DpsStatusRequest"];
export type DpsStatusResponse = components["schemas"]["DpsStatusResponse"];

// Under the settings key, so a save of the FOP form, which may move the registration date, reloads it.
export const dpsStatusQueryKey = ["settings", "dps-status"] as const;

export function useDpsStatus() {
  return useQuery({
    queryKey: dpsStatusQueryKey,
    queryFn: async () => {
      const { data } = await api.GET("/api/settings/dps-status");

      return data;
    },
  });
}

export function useSaveDpsStatus() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (body: DpsStatusRequest) => {
      const { data } = await api.PUT("/api/settings/dps-status", { body });

      return data;
    },
    onSuccess: (saved) => {
      if (saved) {
        queryClient.setQueryData(dpsStatusQueryKey, saved);
      }
      // The dashboard and the declarations sit under the periods key. Like the FOP form's save, a new
      // group 3 start also moves which payments and receipts count.
      queryClient.invalidateQueries({ queryKey: periodsQueryKey });
      queryClient.invalidateQueries({ queryKey: paymentsQueryKey });
      queryClient.invalidateQueries({ queryKey: transactionsQueryKey });
    },
  });
}
