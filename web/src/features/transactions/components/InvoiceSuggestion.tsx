"use client";

import { useLocale, useTranslations } from "next-intl";
import { problemOf } from "@/data/api/client";
import { useApiErrorText } from "@/data/api/useApiErrorText";
import { useLinkReceipt, type InvoiceSummary } from "@/data/invoices/useInvoices";
import { formatDateOnly } from "@/shared/lib/dates";
import { formatAmount } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";

// Nothing here links by itself: each invoice has its own confirm button, and the dismiss only hides the offer.
export function InvoiceSuggestion({
  receiptId,
  invoices,
  rowName,
  receiptClientId,
  onDismiss,
  onFailure,
}: {
  receiptId: string;
  invoices: InvoiceSummary[];
  rowName: string;
  // The receipt's client, by id (a rename must not look like another payer); another client is allowed, with a warning.
  receiptClientId: string | null;
  onDismiss: () => void;
  // Kept by the review screen: a failed link reloads the offers and this panel may unmount with it.
  onFailure: (message: string | null) => void;
}) {
  const t = useTranslations("transactions.row.invoiceSuggestion");
  const apiText = useApiErrorText();
  const locale = useLocale();
  const linkReceipt = useLinkReceipt();

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
            {receiptClientId && receiptClientId !== invoice.clientId ? (
              <span role="note" className="text-xs text-amber-700 dark:text-amber-400">
                {t("payerDiffers")}
              </span>
            ) : null}
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
                  onFailure(null);
                  linkReceipt.mutate(
                    { invoiceId: invoice.id, receiptId },
                    {
                      onError: (error) =>
                        onFailure(problemOf(error)?.status === 409 ? t("conflict") : apiText.withReason(t("failed"), error)),
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
      <div>
        <Button type="button" size="sm" variant="outline" aria-label={`${t("dismiss")}: ${rowName}`} onClick={onDismiss}>
          {t("dismiss")}
        </Button>
      </div>
    </div>
  );
}
