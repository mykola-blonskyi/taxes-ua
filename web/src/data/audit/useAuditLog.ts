"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components } from "@/data/api/schema";

export type AuditedEntity = components["schemas"]["AuditedEntity"];
export type AuditAction = components["schemas"]["AuditAction"];

export type AuditEntryResponse = Omit<components["schemas"]["AuditEntryResponse"], "before" | "after"> & {
  before: Record<string, unknown> | null;
  after: Record<string, unknown> | null;
};

export function useAuditLog(filter: { entity?: AuditedEntity; id?: string }) {
  return useQuery({
    queryKey: ["audit", filter.entity ?? null, filter.id ?? null],
    queryFn: async () => {
      const { data } = await api.GET("/api/audit", {
        params: { query: { entity: filter.entity, id: filter.id } },
      });

      return (data ?? []) as AuditEntryResponse[];
    },
    // The log changes whenever any other screen saves, so a cached page must never be trusted on mount.
    refetchOnMount: "always",
  });
}
