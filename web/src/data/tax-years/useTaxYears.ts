"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";

export type TaxYearConfigRequest = components["schemas"]["TaxYearConfigRequest"];
export type TaxYearConfigResponse = components["schemas"]["TaxYearConfigResponse"];

export const taxYearsQueryKey = ["tax-years"] as const;

export function useTaxYears() {
  return useQuery({
    queryKey: taxYearsQueryKey,
    queryFn: async () => {
      const { data } = await api.GET("/api/tax-years");

      return data;
    },
  });
}

export function useSaveTaxYear() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ year, body }: { year: number; body: TaxYearConfigRequest }) => {
      const { data } = await api.PUT("/api/tax-years/{year}", {
        params: { path: { year } },
        body,
      });

      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: taxYearsQueryKey });
    },
  });
}

export function useVerifyTaxYear() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (year: number) =>
      api.POST("/api/tax-years/{year}/verify", { params: { path: { year } } }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: taxYearsQueryKey });
    },
  });
}

export function useCloneTaxYear() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ year, next }: { year: number; next: number }) =>
      api.POST("/api/tax-years/{year}/clone-to/{next}", { params: { path: { year, next } } }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: taxYearsQueryKey });
    },
  });
}
