"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";

export type SettingsRequest = components["schemas"]["SettingsRequest"];
export type SettingsResponse = components["schemas"]["SettingsResponse"];

export const settingsQueryKey = ["settings"] as const;

export function useSettings() {
  return useQuery({
    queryKey: settingsQueryKey,
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
    },
  });
}
