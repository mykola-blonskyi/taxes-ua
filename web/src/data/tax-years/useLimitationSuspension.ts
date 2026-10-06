"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";
import { keepUntilQueryKey } from "@/data/declarations/useKeepUntil";

export type LimitationSuspension = components["schemas"]["LimitationSuspensionResponse"];
export type LimitationSuspensionRequest = components["schemas"]["LimitationSuspensionRequest"];

export const limitationSuspensionQueryKey = ["limitation-suspension"] as const;

export function useLimitationSuspension() {
  return useQuery({
    queryKey: limitationSuspensionQueryKey,
    queryFn: async () => {
      const { data } = await api.GET("/api/limitation-suspension");

      return data;
    },
  });
}

export function useSaveLimitationSuspension() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (body: LimitationSuspensionRequest) => {
      const { data } = await api.PUT("/api/limitation-suspension", { body });

      return data;
    },
    onSuccess: (data) => {
      queryClient.setQueryData(limitationSuspensionQueryKey, data);
      queryClient.invalidateQueries({ queryKey: keepUntilQueryKey });
    },
  });
}
