import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, readProblem } from "@/data/api/client";
import type { components } from "@/data/api/schema";
import { declarationDetailsQueryKey } from "@/data/declarations/useDeclarations";
import { periodsQueryKey } from "@/data/periods/usePeriods";

export type InvoicingDetailsRequest = components["schemas"]["InvoicingDetailsRequest"];
export type InvoicingDetailsResponse = components["schemas"]["InvoicingDetailsResponse"];
export type PaymentDetailsInput = components["schemas"]["PaymentDetailsInput"];
export type MonobankPrefillResponse = components["schemas"]["MonobankPrefillResponse"];

export const invoicingQueryKey = ["settings", "invoicing"] as const;

// The api's own cap (InvoicingEndpoints.MaxSignatureBytes), checked here so an oversized file is refused
// before it is uploaded.
export const maxSignatureBytes = 512 * 1024;

export const signatureTypes = ["image/png", "image/jpeg"] as const;

export const signatureUrl = "/api/settings/invoicing/signature" as const;

export function useInvoicingDetails() {
  return useQuery({
    queryKey: invoicingQueryKey,
    queryFn: async () => {
      const { data } = await api.GET("/api/settings/invoicing");

      return data;
    },
  });
}

export function useSaveInvoicingDetails() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (body: InvoicingDetailsRequest) => {
      const { data } = await api.PUT("/api/settings/invoicing", { body });

      return data;
    },
    // The declaration takes its name and RNOKPP from the invoicing details.
    onSuccess: (data) => {
      queryClient.setQueryData(invoicingQueryKey, data);
      queryClient.invalidateQueries({ queryKey: declarationDetailsQueryKey });
      queryClient.invalidateQueries({ queryKey: periodsQueryKey });
    },
  });
}

export function useUploadSignature() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (file: File) => {
      const response = await fetch(signatureUrl, {
        method: "PUT",
        headers: { "Content-Type": file.type },
        body: file,
      });

      if (!response.ok) {
        throw await readProblem(response);
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: invoicingQueryKey }),
  });
}

export function useDeleteSignature() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      await api.DELETE("/api/settings/invoicing/signature");
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: invoicingQueryKey }),
  });
}

export function usePrefillFromMonobank() {
  return useMutation({
    mutationFn: async () => {
      const { data } = await api.POST("/api/settings/invoicing/prefill-from-monobank");

      return data;
    },
  });
}
