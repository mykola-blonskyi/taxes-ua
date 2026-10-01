"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ApiError, api } from "@/data/api/client";
import type { components } from "@/data/api/schema";
import { dashboardQueryKey } from "@/data/dashboard/useDashboard";

export type ReserveJar = components["schemas"]["ReserveJarResponse"];
export type JarChoice = components["schemas"]["JarChoiceResponse"];

export const reserveJarQueryKey = ["monobank", "reserve-jar"] as const;

// What the owner can do about a failed call: wait a minute (429), choose again (400 and 409), or try later.
export function jarErrorKind(error: unknown): "wait" | "conflict" | "unavailable" | "failed" {
  if (!(error instanceof ApiError)) {
    return "failed";
  }

  if (error.status === 429) {
    return "wait";
  }

  if (error.status === 400 || error.status === 409) {
    return "conflict";
  }

  return error.status === 502 ? "unavailable" : "failed";
}

// The stored choice and its last balance; reading it never asks the bank.
export function useReserveJar() {
  return useQuery({
    queryKey: reserveJarQueryKey,
    queryFn: async () => {
      const { data } = await api.GET("/api/monobank/reserve-jar");

      return data?.jar ?? null;
    },
    retry: false,
  });
}

// A mutation and not a query: it asks the bank, which allows one such call a minute, so it runs when the
// owner asks and never on a refocus or a remount.
export function useLoadJars() {
  return useMutation({
    mutationFn: async () => {
      const { data } = await api.GET("/api/monobank/jars");

      return data;
    },
  });
}

function useStoreJar() {
  const queryClient = useQueryClient();

  return (jar: ReserveJar | null) => {
    queryClient.setQueryData(reserveJarQueryKey, jar);
    queryClient.invalidateQueries({ queryKey: dashboardQueryKey });
  };
}

export function useChooseReserveJar() {
  const store = useStoreJar();

  return useMutation({
    mutationFn: async (jarId: string) => {
      const { data } = await api.PUT("/api/monobank/reserve-jar", { body: { jarId } });

      return data ?? null;
    },
    onSuccess: store,
  });
}

export function useRefreshReserveJar() {
  const store = useStoreJar();

  return useMutation({
    mutationFn: async () => {
      const { data } = await api.POST("/api/monobank/reserve-jar/refresh");

      return data ?? null;
    },
    onSuccess: store,
  });
}

export function useClearReserveJar() {
  const store = useStoreJar();

  return useMutation({
    mutationFn: async () => {
      await api.DELETE("/api/monobank/reserve-jar");
    },
    onSuccess: () => store(null),
  });
}
