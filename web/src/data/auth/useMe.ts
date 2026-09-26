"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "@/data/api/client";

export const meQueryKey = ["me"] as const;

export function useMe() {
  return useQuery({
    queryKey: meQueryKey,
    queryFn: async () => {
      const { data } = await api.GET("/api/auth/me");

      return data;
    },
    retry: false,
  });
}
