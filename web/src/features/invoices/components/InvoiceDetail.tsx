"use client";

import { useState } from "react";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { Download, ExternalLink } from "lucide-react";
import { ApiError } from "@/data/api/client";
import {
  invoicePdfUrl,
  useCancelInvoice,
  useDuplicateInvoice,
  type InvoiceResponse,
} from "@/data/invoices/useInvoices";
import { formatDateOnly, formatInstantInKyiv } from "@/shared/lib/dates";
import { formatAmount } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";
import { TextAreaField } from "@/shared/ui/fields";
import { InvoiceStatusBadge } from "./InvoiceStatusBadge";

export function InvoiceDetail({
  invoice,
  onBack,
  onOpen,
}: {
  invoice: InvoiceResponse;
  onBack: () => void;
  onOpen: (id: string) => void;
}) {
  const t = useTranslations("invoices.detail");
  const tErrors = useTranslations("invoices.errors");
  const tUnits = useTranslations("invoices.units");
  const locale = useLocale();
  const cancelInvoice = useCancelInvoice();
  const duplicateInvoice = useDuplicateInvoice();
  const [cancelling, setCancelling] = useState(false);
  const [confirmingCancel, setConfirmingCancel] = useState(false);
  const [reason, setReason] = useState("");
  const [failure, setFailure] = useState<string | null>(null);

  const pdfUrl = invoicePdfUrl(invoice.id);
  const reasonMissing = reason.trim() === "";

  function report(error: unknown) {
    setFailure(error instanceof ApiError && error.status === 409 ? tErrors("conflict") : tErrors("generic"));
  }

  function cancel() {
    setFailure(null);
    cancelInvoice.mutate(
      { id: invoice.id, reason: reason.trim() },
      {
        onSuccess: () => {
          setCancelling(false);
          setConfirmingCancel(false);
          setReason("");
        },
        onError: report,
      },
    );
  }

  function duplicate() {
    setFailure(null);
    duplicateInvoice.mutate(invoice.id, {
      onSuccess: (copy) => {
        if (copy) {
          onOpen(copy.id);
        }
      },
      onError: report,
    });
  }

  const summary: [string, string][] = [
    [t("client"), invoice.clientName],
    [t("issueDate"), formatDateOnly(invoice.issueDate, locale)],
    [t("dueDate"), formatDateOnly(invoice.dueDate, locale)],
  ];

  return (
    <div className="flex min-w-0 max-w-2xl flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Button type="button" variant="outline" size="sm" onClick={onBack}>
          {t("back")}
        </Button>
        <InvoiceStatusBadge status={invoice.status} />
      </div>

      <h3 className="break-words text-lg font-semibold">{invoice.number ?? t("noNumber")}</h3>

      <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-1 text-sm">
        {summary.map(([label, value]) => (
          <div key={label} className="contents">
            <dt className="text-muted-foreground">{label}</dt>
            <dd className="break-words">{value}</dd>
          </div>
        ))}
        {invoice.issuedAt ? (
          <div className="contents">
            <dt className="text-muted-foreground">{t("issuedAt")}</dt>
            <dd>{formatInstantInKyiv(invoice.issuedAt, locale)}</dd>
          </div>
        ) : null}
        {invoice.cancelledAt ? (
          <div className="contents">
            <dt className="text-muted-foreground">{t("cancelledAt")}</dt>
            <dd>{formatInstantInKyiv(invoice.cancelledAt, locale)}</dd>
          </div>
        ) : null}
      </dl>

      {invoice.status === "Cancelled" && invoice.cancelReason ? (
        <p className="break-words rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm">
          <span className="font-medium">{t("cancelReason")}: </span>
          {invoice.cancelReason}
        </p>
      ) : null}

      <ul className="flex flex-col gap-2">
        {invoice.lines.map((line, index) => (
          <li key={index} className="flex min-w-0 flex-col gap-1 rounded-lg border p-3 text-sm">
            <span className="break-words">{line.descriptionUk}</span>
            <span className="break-words text-muted-foreground">{line.descriptionEn}</span>
            <span className="flex min-w-0 flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
              <span className="text-xs text-muted-foreground">
                {t("lineFigures", {
                  quantity: new Intl.NumberFormat(locale, { maximumFractionDigits: 3 }).format(
                    Number(line.quantityThousandths) / 1000,
                  ),
                  unit: tUnits(line.unit),
                  rate: formatAmount(Number(line.rateMinor), invoice.currency, locale),
                })}
              </span>
              <span className="font-medium tabular-nums">
                {formatAmount(Number(line.amountMinor), invoice.currency, locale)}
              </span>
            </span>
          </li>
        ))}
      </ul>

      <p className="flex flex-wrap items-baseline justify-between gap-2 border-t pt-3">
        <span className="text-sm font-medium">{t("total")}</span>
        <span className="text-lg font-semibold tabular-nums">
          {formatAmount(Number(invoice.totalMinor), invoice.currency, locale)}
        </span>
      </p>

      {failure ? <p className="text-sm text-destructive">{failure}</p> : null}

      <div className="flex flex-wrap items-center gap-2">
        <Button asChild variant="outline" size="sm">
          <a href={pdfUrl} download={invoice.pdfFileName}>
            <Download aria-hidden="true" />
            {t("download")}
          </a>
        </Button>
        <Button asChild variant="outline" size="sm">
          <a href={pdfUrl} target="_blank" rel="noopener">
            <ExternalLink aria-hidden="true" />
            {t("open")}
          </a>
        </Button>
        <Button type="button" variant="outline" size="sm" disabled={duplicateInvoice.isPending} onClick={duplicate}>
          {t("duplicate")}
        </Button>
        {invoice.status === "Issued" && !cancelling ? (
          <Button type="button" variant="destructive" size="sm" onClick={() => setCancelling(true)}>
            {t("cancel")}
          </Button>
        ) : null}
        <Link
          href={`/history?entity=Invoice&id=${invoice.id}`}
          className="text-sm text-primary underline-offset-4 hover:underline"
        >
          {t("history")}
        </Link>
      </div>

      {cancelling ? (
        <div className="flex flex-col gap-3 rounded-lg border p-3">
          <TextAreaField
            id="invoice-cancel-reason"
            label={t("reasonLabel")}
            rows={3}
            value={reason}
            onChange={setReason}
          />
          {confirmingCancel ? (
            <div className="flex flex-col gap-2">
              <p className="text-sm">{t("confirmCancel")}</p>
              <div className="flex flex-wrap gap-2">
                <Button
                  type="button"
                  size="sm"
                  variant="destructive"
                  disabled={cancelInvoice.isPending || reasonMissing}
                  onClick={cancel}
                >
                  {t("cancelConfirm")}
                </Button>
                <Button type="button" size="sm" variant="outline" onClick={() => setConfirmingCancel(false)}>
                  {t("keep")}
                </Button>
              </div>
            </div>
          ) : (
            <div className="flex flex-wrap gap-2">
              <Button
                type="button"
                size="sm"
                variant="destructive"
                disabled={reasonMissing}
                onClick={() => setConfirmingCancel(true)}
              >
                {t("cancel")}
              </Button>
              <Button type="button" size="sm" variant="outline" onClick={() => setCancelling(false)}>
                {t("keep")}
              </Button>
            </div>
          )}
        </div>
      ) : null}
    </div>
  );
}
