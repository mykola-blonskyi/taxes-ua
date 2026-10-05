"use client";

import { useState } from "react";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { TriangleAlert } from "lucide-react";
import { useApiErrorText } from "@/data/api/useApiErrorText";
import { useDeletePayment, type PaymentResponse } from "@/data/payments/usePayments";
import { formatDateOnly } from "@/shared/lib/dates";
import { formatMoney } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";
import { monthName } from "../period";

export function PaymentList({
  items,
  onEdit,
}: {
  items: PaymentResponse[];
  onEdit: (payment: PaymentResponse) => void;
}) {
  return (
    <ul className="flex flex-col gap-2">
      {items.map((payment) => (
        <PaymentRow key={payment.id} payment={payment} onEdit={onEdit} />
      ))}
    </ul>
  );
}

function PaymentRow({ payment, onEdit }: { payment: PaymentResponse; onEdit: (payment: PaymentResponse) => void }) {
  const t = useTranslations("payments");
  const apiText = useApiErrorText();
  const locale = useLocale();
  const deletePayment = useDeletePayment();
  const [confirmingDelete, setConfirmingDelete] = useState(false);

  const deleteFailure = deletePayment.error;
  const formattedDate = formatDateOnly(payment.paidOn, locale);
  const formattedAmount = formatMoney(Number(payment.amountKop), locale);
  const period =
    payment.periodMonth !== null
      ? monthName(Number(payment.periodMonth), locale)
      : t("quarter", { quarter: Number(payment.periodQuarter) });
  const rowName = `${formattedDate}, ${t(`kinds.${payment.kind}`)}, ${formattedAmount}`;

  return (
    <li className="flex min-w-0 flex-col gap-1.5 rounded-lg border p-3">
      <div className="flex items-start justify-between gap-2">
        <div className="flex min-w-0 flex-wrap items-baseline gap-2">
          <span className="text-sm font-medium">{formattedDate}</span>
          <span className="text-sm text-muted-foreground">{t(`kinds.${payment.kind}`)}</span>
        </div>
        <span className="shrink-0 text-sm font-semibold">{formattedAmount}</span>
      </div>

      <p className="text-xs text-muted-foreground">
        {t("periodOf", { period, year: Number(payment.periodYear) })}
      </p>

      {payment.note ? <p className="min-w-0 break-words text-xs text-muted-foreground">{payment.note}</p> : null}

      {payment.beforeRegistration ? (
        <p className="flex items-start gap-1.5 text-xs text-destructive">
          <TriangleAlert className="mt-0.5 size-3.5 shrink-0" aria-hidden="true" />
          <span className="min-w-0 break-words">{t("row.beforeRegistrationWarning")}</span>
        </p>
      ) : null}

      <div className="flex flex-wrap items-center gap-2 pt-1">
        {confirmingDelete ? (
          <>
            <span className="text-xs text-destructive">{t("row.confirmDelete")}</span>
            <Button
              type="button"
              variant="destructive"
              size="sm"
              disabled={deletePayment.isPending}
              onClick={() => deletePayment.mutate(payment.id, { onSuccess: () => setConfirmingDelete(false) })}
            >
              {t("row.confirmDeleteYes")}
            </Button>
            <Button type="button" variant="outline" size="sm" onClick={() => setConfirmingDelete(false)}>
              {t("row.confirmDeleteCancel")}
            </Button>
          </>
        ) : (
          <>
            <Button
              type="button"
              variant="outline"
              size="sm"
              aria-label={`${t("row.edit")}: ${rowName}`}
              onClick={() => onEdit(payment)}
            >
              {t("row.edit")}
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              aria-label={`${t("row.delete")}: ${rowName}`}
              onClick={() => setConfirmingDelete(true)}
            >
              {t("row.delete")}
            </Button>
            <Button asChild variant="outline" size="sm">
              <Link
                href={`/history?entity=BudgetPayment&id=${payment.id}`}
                aria-label={`${t("row.history")}: ${rowName}`}
              >
                {t("row.history")}
              </Link>
            </Button>
          </>
        )}
      </div>

      {deleteFailure ? (
        <p className="text-xs text-destructive">{apiText.withReason(t("row.deleteFailed"), deleteFailure)}</p>
      ) : null}
    </li>
  );
}
