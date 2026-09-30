import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/data/api/client";
import type { components, paths } from "@/data/api/schema";

export type InvoiceRequest = components["schemas"]["InvoiceRequest"];
export type InvoiceLineRequest = components["schemas"]["InvoiceLineRequest"];
export type InvoiceResponse = components["schemas"]["InvoiceResponse"];
export type InvoiceLineResponse = components["schemas"]["InvoiceLineResponse"];
export type InvoiceSummary = components["schemas"]["InvoiceSummary"];
export type InvoiceStatus = components["schemas"]["InvoiceStatus"];
export type InvoiceUnit = components["schemas"]["InvoiceUnit"];

export type InvoiceFilter = { status?: InvoiceStatus; year?: number };

export const invoicesQueryKey = ["invoices"] as const;

export const invoiceUnits: readonly InvoiceUnit[] = ["Service", "Hour", "Day", "Month"];

export const maxInvoiceLines = 50;

type PdfPath = Extract<keyof paths, "/api/invoices/{id}/pdf">;

// Typed against the generated schema, so a renamed or removed PDF endpoint fails the build.
export function invoicePdfUrl(id: string): string {
  const path: PdfPath = "/api/invoices/{id}/pdf";

  return path.replace("{id}", encodeURIComponent(id));
}

export function useInvoices(filter: InvoiceFilter) {
  return useQuery({
    queryKey: [...invoicesQueryKey, "list", filter.status ?? null, filter.year ?? null],
    queryFn: async () => {
      const { data } = await api.GET("/api/invoices", {
        params: { query: { status: filter.status, year: filter.year } },
      });

      return data;
    },
  });
}

export function useInvoice(id: string) {
  return useQuery({
    queryKey: [...invoicesQueryKey, "detail", id],
    queryFn: async () => {
      const { data } = await api.GET("/api/invoices/{id}", { params: { path: { id } } });

      return data;
    },
  });
}

function useInvalidateInvoices() {
  const queryClient = useQueryClient();

  return () => queryClient.invalidateQueries({ queryKey: invoicesQueryKey });
}

export function useCreateInvoice() {
  const invalidate = useInvalidateInvoices();

  return useMutation({
    mutationFn: async (body: InvoiceRequest) => {
      const { data } = await api.POST("/api/invoices", { body });

      return data;
    },
    onSuccess: invalidate,
  });
}

export function useUpdateInvoice() {
  const invalidate = useInvalidateInvoices();

  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: InvoiceRequest }) => {
      const { data } = await api.PUT("/api/invoices/{id}", { params: { path: { id } }, body });

      return data;
    },
    onSuccess: invalidate,
  });
}

export function useDeleteInvoice() {
  const invalidate = useInvalidateInvoices();

  return useMutation({
    mutationFn: async (id: string) => {
      await api.DELETE("/api/invoices/{id}", { params: { path: { id } } });
    },
    onSuccess: invalidate,
    // A 409 means the invoice was issued elsewhere, so the cached status is stale.
    onError: invalidate,
  });
}

export function useDuplicateInvoice() {
  const invalidate = useInvalidateInvoices();

  return useMutation({
    mutationFn: async (id: string) => {
      const { data } = await api.POST("/api/invoices/{id}/duplicate", { params: { path: { id } } });

      return data;
    },
    onSuccess: invalidate,
  });
}

export function useIssueInvoice() {
  const invalidate = useInvalidateInvoices();

  return useMutation({
    mutationFn: async (id: string) => {
      const { data } = await api.POST("/api/invoices/{id}/issue", { params: { path: { id } } });

      return data;
    },
    onSuccess: invalidate,
    onError: invalidate,
  });
}

export function useCancelInvoice() {
  const invalidate = useInvalidateInvoices();

  return useMutation({
    mutationFn: async ({ id, reason }: { id: string; reason: string }) => {
      const { data } = await api.POST("/api/invoices/{id}/cancel", {
        params: { path: { id } },
        body: { reason },
      });

      return data;
    },
    onSuccess: invalidate,
    onError: invalidate,
  });
}
