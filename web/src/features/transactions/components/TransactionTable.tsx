"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { TriangleAlert } from "lucide-react";
import { ApiError } from "@/data/api/client";
import { useDeleteTransaction, type TransactionResponse } from "@/data/transactions/useTransactions";
import { formatMoney } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";
import { isNonIncomeKind } from "../kinds";

// Building the Date from Y/M/D parts (instead of parsing the ISO string) avoids a UTC-vs-local
// timezone shift moving the displayed day.
function parseDateOnly(value: string): Date {
  const [year, month, day] = value.split("-").map(Number);

  return new Date(year, month - 1, day);
}

export function TransactionTable({
  items,
  onEdit,
}: {
  items: TransactionResponse[];
  onEdit: (transaction: TransactionResponse) => void;
}) {
  return (
    <ul className="flex flex-col gap-2">
      {items.map((transaction) => (
        <TransactionRow key={transaction.id} transaction={transaction} onEdit={onEdit} />
      ))}
    </ul>
  );
}

function TransactionRow({
  transaction,
  onEdit,
}: {
  transaction: TransactionResponse;
  onEdit: (transaction: TransactionResponse) => void;
}) {
  const t = useTranslations("transactions");
  const tKinds = useTranslations("transactions.kinds");
  const locale = useLocale();
  const deleteTransaction = useDeleteTransaction();
  const [confirmingDelete, setConfirmingDelete] = useState(false);

  const nonIncome = isNonIncomeKind(transaction.kind);
  const amountKop = Number(transaction.amountUahKop);
  const displayAmount = transaction.kind === "RefundToClient" ? -amountKop : amountKop;
  const deleteFailure = deleteTransaction.error instanceof ApiError ? deleteTransaction.error : null;
  const formattedDate = new Intl.DateTimeFormat(locale, { dateStyle: "medium" }).format(
    parseDateOnly(transaction.valueDate),
  );
  const formattedAmount = formatMoney(displayAmount, locale);
  const rowName = `${formattedDate}, ${formattedAmount}`;

  return (
    <li className="flex min-w-0 flex-col gap-1.5 rounded-lg border p-3">
      <div className="flex items-start justify-between gap-2">
        <div className="flex min-w-0 flex-wrap items-baseline gap-2">
          <span className="text-sm font-medium">{formattedDate}</span>
          <span className="text-sm text-muted-foreground">{tKinds(transaction.kind)}</span>
        </div>
        <span className={nonIncome ? "shrink-0 text-sm text-muted-foreground" : "shrink-0 text-sm font-semibold"}>
          {formattedAmount}
        </span>
      </div>

      <div className="flex flex-wrap gap-2 text-xs text-muted-foreground">
        {nonIncome ? <span className="rounded bg-muted px-1.5 py-0.5">{t("row.nonIncomeTag")}</span> : null}
        {transaction.clientName ? <span className="min-w-0 break-words">{transaction.clientName}</span> : null}
        {transaction.invoiceNumber ? (
          <span className="min-w-0 break-words">{transaction.invoiceNumber}</span>
        ) : null}
      </div>

      {transaction.description ? (
        <p className="min-w-0 break-words text-xs text-muted-foreground">{transaction.description}</p>
      ) : null}

      {nonIncome && transaction.nonIncomeReason ? (
        <p className="min-w-0 break-words text-xs text-muted-foreground">{transaction.nonIncomeReason}</p>
      ) : null}

      {transaction.beforeRegistration ? (
        <p className="flex items-center gap-1.5 text-xs text-destructive">
          <TriangleAlert className="size-3.5 shrink-0" aria-hidden="true" />
          <span className="min-w-0 break-words">{t("row.beforeRegistration")}</span>
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
              disabled={deleteTransaction.isPending}
              onClick={() =>
                deleteTransaction.mutate(transaction.id, { onSuccess: () => setConfirmingDelete(false) })
              }
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
              onClick={() => onEdit(transaction)}
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
          </>
        )}
      </div>

      {deleteFailure ? (
        <p className="text-xs text-destructive">{`${t("row.deleteFailed")} ${deleteFailure.message}`}</p>
      ) : null}
    </li>
  );
}
