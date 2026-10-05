"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { LoadState } from "@/data/api/LoadState";
import { problemOf } from "@/data/api/client";
import { useApiErrorText } from "@/data/api/useApiErrorText";
import {
  useLinkReceipt,
  useReceiptOptions,
  useUnlinkReceipt,
  type InvoiceResponse,
} from "@/data/invoices/useInvoices";
import { formatDateOnly } from "@/shared/lib/dates";
import { formatAmount } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";

export function InvoicePayments({ invoice }: { invoice: InvoiceResponse }) {
  const t = useTranslations("invoices.payments");
  const apiText = useApiErrorText();
  const locale = useLocale();
  const linkReceipt = useLinkReceipt();
  const unlinkReceipt = useUnlinkReceipt();
  const [picking, setPicking] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  const paid = invoice.standing === "Paid";
  const canLink = invoice.status === "Issued" && !paid;
  const optionsQuery = useReceiptOptions(invoice.id, canLink && picking);
  const { data: options } = optionsQuery;
  const format = (amountMinor: number | string) => formatAmount(Number(amountMinor), invoice.currency, locale);

  function report(error: unknown) {
    setFailure(problemOf(error)?.status === 409 ? t("conflict") : apiText.withReason(t("failed"), error));
  }

  function link(receiptId: string) {
    setFailure(null);
    linkReceipt.mutate(
      { invoiceId: invoice.id, receiptId },
      { onSuccess: () => setPicking(false), onError: report },
    );
  }

  function unlink(receiptId: string) {
    setFailure(null);
    unlinkReceipt.mutate({ invoiceId: invoice.id, receiptId }, { onError: report });
  }

  return (
    <section className="flex flex-col gap-3 border-t pt-3">
      <h4 className="text-sm font-semibold">{t("title")}</h4>

      <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-1 text-sm">
        <dt className="text-muted-foreground">{t("paid")}</dt>
        <dd className="text-right tabular-nums">{format(invoice.paidMinor)}</dd>
        {invoice.dueMinor !== null ? (
          <>
            <dt className="text-muted-foreground">{t("due")}</dt>
            <dd className="text-right font-medium tabular-nums">{format(invoice.dueMinor)}</dd>
          </>
        ) : null}
      </dl>

      {invoice.receipts.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t("empty")}</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {invoice.receipts.map((receipt) => (
            <li key={receipt.id} className="flex min-w-0 flex-col gap-1 rounded-lg border p-3 text-sm">
              <span className="flex min-w-0 flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
                <span>{formatDateOnly(receipt.valueDate, locale)}</span>
                <span className="font-medium tabular-nums">{format(receipt.amountMinor)}</span>
              </span>
              {receipt.clientName ? (
                <span className="break-words text-muted-foreground">{receipt.clientName}</span>
              ) : null}
              {Number(receipt.refundedMinor) > 0 ? (
                <span className="text-xs text-muted-foreground">
                  {t("refunded", { amount: format(receipt.refundedMinor) })}
                </span>
              ) : null}
              <div>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={unlinkReceipt.isPending}
                  onClick={() => unlink(receipt.id)}
                >
                  {t("unlink")}
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}

      {failure ? <p className="text-sm text-destructive">{failure}</p> : null}

      {canLink && !picking ? (
        <div>
          <Button type="button" size="sm" onClick={() => setPicking(true)}>
            {t("link")}
          </Button>
        </div>
      ) : null}

      {canLink && picking ? (
        <div className="flex flex-col gap-2 rounded-lg border p-3">
          <p className="text-sm font-medium">{t("pick")}</p>
          {!options ? (
            <LoadState query={optionsQuery} loading={t("loadingOptions")} failed={t("failed")} />
          ) : null}
          {options && options.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t("noOptions")}</p>
          ) : null}
          {options && options.length > 0 ? (
            <ul className="flex flex-col gap-2">
              {options.map((option) => (
                <li key={option.id}>
                  <button
                    type="button"
                    disabled={linkReceipt.isPending}
                    onClick={() => link(option.id)}
                    className="flex w-full min-w-0 flex-col gap-1 rounded-lg border p-3 text-left text-sm hover:bg-muted focus-visible:border-ring focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-ring disabled:opacity-50"
                  >
                    <span className="flex min-w-0 flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
                      <span>{formatDateOnly(option.valueDate, locale)}</span>
                      <span className="font-medium tabular-nums">{format(option.amountMinor)}</span>
                    </span>
                    {option.clientName ? (
                      <span className="break-words text-muted-foreground">{option.clientName}</span>
                    ) : null}
                    {option.clientId && option.clientId !== invoice.clientId ? (
                      <span role="note" className="text-xs text-amber-700 dark:text-amber-400">
                        {t("payerDiffers")}
                      </span>
                    ) : null}
                  </button>
                </li>
              ))}
            </ul>
          ) : null}
          <div>
            <Button type="button" size="sm" variant="outline" onClick={() => setPicking(false)}>
              {t("closePicker")}
            </Button>
          </div>
        </div>
      ) : null}
    </section>
  );
}
