"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";

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
