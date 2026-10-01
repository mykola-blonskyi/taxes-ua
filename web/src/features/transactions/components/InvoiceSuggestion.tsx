"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import { useLinkReceipt, type InvoiceSummary } from "@/data/invoices/useInvoices";
import { formatDateOnly } from "@/shared/lib/dates";
import { formatAmount } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";

// Nothing here links by itself: each invoice has its own confirm button, and the dismiss only hides the offer.
export function InvoiceSuggestion({
  receiptId,
  invoices,
  rowName,
  onDismiss,
}: {
  receiptId: string;
  invoices: InvoiceSummary[];
  rowName: string;
  onDismiss: () => void;
}) {
  const t = useTranslations("transactions.row.invoiceSuggestion");
  const locale = useLocale();
  const linkReceipt = useLinkReceipt();
  const [failure, setFailure] = useState<string | null>(null);

  return (
    <div className="flex min-w-0 flex-col gap-2 rounded-lg border border-emerald-500/40 bg-emerald-500/5 p-3">
      <p className="text-sm font-medium">{t(invoices.length === 1 ? "titleOne" : "titleMany")}</p>
      <ul className="flex flex-col gap-2">
        {invoices.map((invoice) => (
          <li key={invoice.id} className="flex min-w-0 flex-col gap-1 rounded-lg border bg-background p-3 text-sm">
            <span className="flex min-w-0 flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
              <span className="break-words font-medium">{invoice.number ?? ""}</span>
              {invoice.dueMinor !== null ? (
                <span className="font-medium tabular-nums">
                  {formatAmount(Number(invoice.dueMinor), invoice.currency, locale)}
                </span>
              ) : null}
            </span>
            <span className="break-words text-muted-foreground">{invoice.clientName}</span>
            <span className="text-xs text-muted-foreground">
              {t("dueDate", { date: formatDateOnly(invoice.dueDate, locale) })}
            </span>
            <div className="pt-1">
              <Button
                type="button"
                size="sm"
                aria-label={`${t("confirm")} ${invoice.number ?? ""}: ${rowName}`}
                disabled={linkReceipt.isPending}
                onClick={() => {
                  setFailure(null);
                  linkReceipt.mutate(
                    { invoiceId: invoice.id, receiptId },
                    {
                      onError: (error) =>
                        setFailure(error instanceof ApiError && error.status === 409 ? t("conflict") : t("failed")),
                    },
                  );
                }}
              >
                {t("confirm")}
              </Button>
            </div>
          </li>
        ))}
      </ul>
      {failure ? <p className="text-xs text-destructive">{failure}</p> : null}
      <div>
        <Button type="button" size="sm" variant="outline" aria-label={`${t("dismiss")}: ${rowName}`} onClick={onDismiss}>
          {t("dismiss")}
        </Button>
      </div>
    </div>
  );
}
