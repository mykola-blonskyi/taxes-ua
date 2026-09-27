import type { paths } from "@/data/api/schema";

export type ExportFormat = "csv" | "xlsx" | "pdf";

type ExportPath = Extract<keyof paths, `/api/export/transactions.${ExportFormat}`>;

// Typed against the generated schema, so a renamed or removed export endpoint fails the build.
export function transactionsExportUrl(format: ExportFormat, year: number): string {
  const path: ExportPath = `/api/export/transactions.${format}`;

  return `${path}?year=${year}`;
}
