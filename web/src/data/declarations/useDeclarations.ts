"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "@/data/api/client";
import type { components, paths } from "@/data/api/schema";
import { periodsQueryKey } from "@/data/periods/usePeriods";

export type DeclarationResponse = components["schemas"]["DeclarationResponse"];
export type DeclarationFigures = components["schemas"]["DeclarationFiguresResponse"];
export type DeclarationReadiness = components["schemas"]["DeclarationReadinessResponse"];
export type DeclarationFiling = components["schemas"]["DeclarationFilingResponse"];
export type DeclarationFile = components["schemas"]["DeclarationFileResponse"];
export type DeclarationType = components["schemas"]["DeclarationType"];
export type DeclarationDetailField = DeclarationReadiness["missingDetails"][number];
export type DeclarationDetailsRequest = components["schemas"]["DeclarationDetailsRequest"];
export type DeclarationDetailsResponse = components["schemas"]["DeclarationDetailsResponse"];

// Nested under the periods key like the dashboard: the readiness counts the same receipts, payments,
// candidates and settings, so every write that invalidates the periods refreshes the declaration too.
const declarationsQueryKey = [...periodsQueryKey, "declarations"] as const;

export const declarationDetailsQueryKey = ["settings", "declaration"] as const;

export function useDeclaration(year: number, quarter: number) {
  return useQuery({
    queryKey: [...declarationsQueryKey, year, quarter],
    queryFn: async () => {
      const { data } = await api.GET("/api/declarations/{year}/{quarter}", {
        params: { path: { year, quarter } },
      });

      return data;
    },
    retry: (failureCount, error) => !(error instanceof ApiError && error.status === 404) && failureCount < 1,
  });
}

type FilePath = Extract<keyof paths, "/api/declarations/{year}/{quarter}/files/{type}">;

// Typed against the generated schema, so a renamed or removed download endpoint fails the build.
export function declarationFileUrl(year: number, quarter: number, type: DeclarationType): string {
  const path: FilePath = "/api/declarations/{year}/{quarter}/files/{type}";

  return path
    .replace("{year}", String(year))
    .replace("{quarter}", String(quarter))
    .replace("{type}", encodeURIComponent(type));
}

export function useGenerateDeclarationFile(year: number, quarter: number) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (type: DeclarationType) => {
      const { data } = await api.POST("/api/declarations/{year}/{quarter}/files", {
        params: { path: { year, quarter } },
        body: { type },
      });

      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...declarationsQueryKey, year, quarter] }),
  });
}

export function useMarkDeclarationFiled(year: number, quarter: number) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (body: { filedOn: string; type: DeclarationType }) => {
      const { data } = await api.PUT("/api/declarations/{year}/{quarter}/filing", {
        params: { path: { year, quarter } },
        body,
      });

      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: periodsQueryKey }),
  });
}

export function useUndoDeclarationFiling(year: number, quarter: number) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      await api.DELETE("/api/declarations/{year}/{quarter}/filing", { params: { path: { year, quarter } } });
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: periodsQueryKey }),
  });
}

export function useDeclarationDetails() {
  return useQuery({
    queryKey: declarationDetailsQueryKey,
    queryFn: async () => {
      const { data } = await api.GET("/api/settings/declaration");

      return data;
    },
  });
}

export function useSaveDeclarationDetails() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (body: DeclarationDetailsRequest) => {
      const { data } = await api.PUT("/api/settings/declaration", { body });

      return data;
    },
    onSuccess: (data) => {
      queryClient.setQueryData(declarationDetailsQueryKey, data);
      queryClient.invalidateQueries({ queryKey: periodsQueryKey });
    },
  });
}
