"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { LoadState } from "@/data/api/LoadState";
import { ApiError } from "@/data/api/client";
import { useLinkReceipt, usePayableInvoices, useUnlinkReceipt } from "@/data/invoices/useInvoices";
import type { TransactionResponse } from "@/data/transactions/useTransactions";
import { formatDateOnly } from "@/shared/lib/dates";
import { formatAmount } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";

// Rendered inside the row's wrapping action bar; the picker and any failure take a full line of it.
export function InvoiceLinkAction({ transaction, rowName }: { transaction: TransactionResponse; rowName: string }) {
  const t = useTranslations("transactions.row.invoiceLink");
  const locale = useLocale();
  const linkReceipt = useLinkReceipt();
  const unlinkReceipt = useUnlinkReceipt();
  const [picking, setPicking] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const optionsQuery = usePayableInvoices(transaction.id, picking);
  const { data: options } = optionsQuery;

  function report(error: unknown) {
    setFailure(error instanceof ApiError && error.status === 409 ? t("conflict") : t("failed"));
  }

  if (transaction.invoiceId) {
    const invoiceId = transaction.invoiceId;

    return (
      <>
        <Button
          type="button"
          variant="outline"
          size="sm"
          aria-label={`${t("unlink")}: ${rowName}`}
          disabled={unlinkReceipt.isPending}
          onClick={() => {
            setFailure(null);
            unlinkReceipt.mutate({ invoiceId, receiptId: transaction.id }, { onError: report });
          }}
        >
          {t("unlink")}
        </Button>
        {failure ? <p className="w-full basis-full text-xs text-destructive">{failure}</p> : null}
      </>
    );
  }

  return (
    <>
      {picking ? null : (
        <Button
          type="button"
          variant="outline"
          size="sm"
          aria-label={`${t("link")}: ${rowName}`}
          onClick={() => setPicking(true)}
        >
          {t("link")}
        </Button>
      )}
      {failure ? <p className="w-full basis-full text-xs text-destructive">{failure}</p> : null}
      {picking ? (
        <div className="flex w-full basis-full flex-col gap-2 rounded-lg border p-3">
          <p className="text-sm font-medium">{t("pick")}</p>
          {optionsQuery.isLoading || optionsQuery.isError ? (
            <LoadState query={optionsQuery} loading={t("loading")} failed={t("failed")} />
          ) : null}
          {options && options.length === 0 ? <p className="text-xs text-muted-foreground">{t("none")}</p> : null}
          {options && options.length > 0 ? (
            <ul className="flex flex-col gap-2">
              {options.map((invoice) => (
                <li key={invoice.id}>
                  <button
                    type="button"
                    disabled={linkReceipt.isPending}
                    onClick={() => {
                      setFailure(null);
                      linkReceipt.mutate(
                        { invoiceId: invoice.id, receiptId: transaction.id },
                        { onSuccess: () => setPicking(false), onError: report },
                      );
                    }}
                    className="flex w-full min-w-0 flex-col gap-1 rounded-lg border p-3 text-left text-sm hover:bg-muted focus-visible:border-ring focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-ring/50 disabled:opacity-50"
                  >
                    <span className="flex min-w-0 flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
                      <span className="break-words font-medium">{invoice.number ?? ""}</span>
                      {invoice.dueMinor !== null ? (
                        <span className="font-medium tabular-nums">
                          {formatAmount(Number(invoice.dueMinor), invoice.currency, locale)}
                        </span>
                      ) : null}
                    </span>
                    <span className="break-words text-muted-foreground">{invoice.clientName}</span>
                    {transaction.clientId && transaction.clientId !== invoice.clientId ? (
                      <span role="note" className="text-xs text-amber-700 dark:text-amber-400">
                        {t("payerDiffers")}
                      </span>
                    ) : null}
                    <span className="text-xs text-muted-foreground">
                      {t("dueDate", { date: formatDateOnly(invoice.dueDate, locale) })}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          ) : null}
          <div>
            <Button type="button" size="sm" variant="outline" onClick={() => setPicking(false)}>
              {t("close")}
            </Button>
          </div>
        </div>
      ) : null}
    </>
  );
}
