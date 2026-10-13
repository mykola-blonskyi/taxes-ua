"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";
import { dashboardQueryKey } from "@/data/dashboard/useDashboard";

export type ArchiveResponse = components["schemas"]["ArchiveResponse"];

export function useArchive(year: number | undefined) {
  return useQuery({
    queryKey: ["archive", year],
    queryFn: async () => {
      const { data } = await api.GET("/api/archive/{year}", { params: { path: { year: year! } } });

      return data;
    },
    enabled: year !== undefined,
    // What the year holds changes whenever another screen saves, so a cached listing is never trusted on mount.
    refetchOnMount: "always",
  });
}

// A finished ZIP answers the dashboard's archive notice on the server, so the cached dashboard is stale from
// the moment a download starts.
export function useArchiveDownloaded() {
  const queryClient = useQueryClient();

  return () => queryClient.invalidateQueries({ queryKey: dashboardQueryKey });
}

export function useDismissArchiveNotice() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (year: number) => {
      await api.POST("/api/archive/{year}/notice/dismiss", { params: { path: { year } } });
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: dashboardQueryKey }),
  });
}
