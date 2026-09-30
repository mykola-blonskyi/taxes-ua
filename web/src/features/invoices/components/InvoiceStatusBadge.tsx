import { useTranslations } from "next-intl";
import type { InvoiceStanding } from "@/data/invoices/useInvoices";
import { cn } from "@/shared/lib/utils";

const tones: Record<InvoiceStanding, string> = {
  Draft: "bg-muted text-muted-foreground",
  Issued: "bg-blue-500/15 text-blue-700 dark:text-blue-400",
  Overdue: "bg-amber-500/15 text-amber-700 dark:text-amber-400",
  Paid: "bg-emerald-500/15 text-emerald-700 dark:text-emerald-400",
  Cancelled: "bg-destructive/10 text-destructive",
};

export function InvoiceStatusBadge({ standing }: { standing: InvoiceStanding }) {
  const t = useTranslations("invoices.status");

  return (
    <span className={cn("inline-flex shrink-0 rounded-full px-2 py-0.5 text-xs font-medium", tones[standing])}>
      {t(standing)}
    </span>
  );
}
