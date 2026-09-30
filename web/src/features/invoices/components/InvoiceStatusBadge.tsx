import { useTranslations } from "next-intl";
import type { InvoiceStatus } from "@/data/invoices/useInvoices";
import { cn } from "@/shared/lib/utils";

const tones: Record<InvoiceStatus, string> = {
  Draft: "bg-muted text-muted-foreground",
  Issued: "bg-emerald-500/15 text-emerald-700 dark:text-emerald-400",
  Cancelled: "bg-destructive/10 text-destructive",
};

export function InvoiceStatusBadge({ status }: { status: InvoiceStatus }) {
  const t = useTranslations("invoices.status");

  return (
    <span className={cn("inline-flex shrink-0 rounded-full px-2 py-0.5 text-xs font-medium", tones[status])}>
      {t(status)}
    </span>
  );
}
