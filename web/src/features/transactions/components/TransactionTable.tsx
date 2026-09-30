"use client";

import { useState } from "react";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { TriangleAlert } from "lucide-react";
import { ApiError } from "@/data/api/client";
import {
  useConfirmTransaction,
  useDeleteTransaction,
  type TransactionResponse,
} from "@/data/transactions/useTransactions";
import { formatDateOnly, formatNumericDate } from "@/shared/lib/dates";
import { formatAmount, formatMinor, formatMoney, formatRateE4 } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";
import { isNonIncomeKind } from "../kinds";

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
  const confirmTransaction = useConfirmTransaction();
  const [confirmingDelete, setConfirmingDelete] = useState(false);

  const nonIncome = isNonIncomeKind(transaction.kind);
  const amountKop = Number(transaction.amountUahKop);
  const displayAmount = transaction.kind === "RefundToClient" ? -amountKop : amountKop;
  const deleteFailure = deleteTransaction.error instanceof ApiError ? deleteTransaction.error : null;
  const confirmFailure = confirmTransaction.error instanceof ApiError ? confirmTransaction.error : null;
  const needsReview = transaction.reviewStatus === "NeedsReview";
  const formattedDate = formatDateOnly(transaction.valueDate, locale);
  const receipt = transaction.refundsReceipt;
  const formattedAmount = formatMoney(displayAmount, locale);
  const rowName = `${formattedDate}, ${formattedAmount}`;
  const rateSourceText =
    transaction.rateSource === "Manual"
      ? t("row.rateManual")
      : transaction.rateDate
        ? t("row.rateNbu", { date: formatNumericDate(transaction.rateDate, locale) })
        : null;

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

      {transaction.currency !== "UAH" ? (
        <p className="min-w-0 break-words text-xs text-muted-foreground">
          {`${formatMinor(Number(transaction.amountMinor), transaction.currency, locale)} × ${formatRateE4(Number(transaction.rateE4), locale)}`}
          {rateSourceText ? ` · ${rateSourceText}` : null}
        </p>
      ) : null}

      <div className="flex flex-wrap gap-2 text-xs text-muted-foreground">
        {needsReview ? (
          <span className="rounded bg-muted px-1.5 py-0.5 font-medium text-destructive">
            {t("row.needsReview")}
          </span>
        ) : null}
        {nonIncome ? <span className="rounded bg-muted px-1.5 py-0.5">{t("row.nonIncomeTag")}</span> : null}
        {transaction.source ? (
          <span className="rounded bg-muted px-1.5 py-0.5">
            {t(`row.source.${transaction.source.bank}`, { currency: transaction.source.accountCurrency })}
          </span>
        ) : null}
        {transaction.clientName ? <span className="min-w-0 break-words">{transaction.clientName}</span> : null}
        {transaction.invoiceNumber ? (
          <span className="min-w-0 break-words">{transaction.invoiceNumber}</span>
        ) : null}
      </div>

      {transaction.setAside && !nonIncome ? (
        <p className="min-w-0 break-words text-xs text-muted-foreground">
          {t(transaction.kind === "RefundToClient" ? "row.setAsideRelease" : "row.setAside", {
            singleTax: formatMoney(Math.abs(Number(transaction.setAside.singleTaxKop)), locale),
            levy: formatMoney(Math.abs(Number(transaction.setAside.militaryLevyKop)), locale),
          })}
        </p>
      ) : null}

      {transaction.description ? (
        <p className="min-w-0 break-words text-xs text-muted-foreground">{transaction.description}</p>
      ) : null}

      {nonIncome && transaction.nonIncomeReason ? (
        <p className="min-w-0 break-words text-xs text-muted-foreground">{transaction.nonIncomeReason}</p>
      ) : null}

      {receipt ? (
        <p className="min-w-0 break-words text-xs text-muted-foreground">
          {t("row.reversesReceipt", {
            date: formatDateOnly(receipt.valueDate, locale),
            amount: formatAmount(Number(receipt.amountMinor), receipt.currency, locale),
          })}
        </p>
      ) : null}

      {transaction.beforeRegistration ? (
        <p className="flex items-center gap-1.5 text-xs text-destructive">
          <TriangleAlert className="size-3.5 shrink-0" aria-hidden="true" />
          <span className="min-w-0 break-words">
            {receipt ? t("row.reversesReceiptBeforeRegistration") : t("row.beforeRegistration")}
          </span>
        </p>
      ) : null}

      <div className="flex flex-wrap items-center gap-2 pt-1">
        {confirmingDelete ? (
          <>
            <span className="text-xs text-destructive">
              {t(transaction.source ? "row.confirmDismiss" : "row.confirmDelete")}
            </span>
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
            {needsReview ? (
              <Button
                type="button"
                size="sm"
                aria-label={`${t("row.confirm")}: ${rowName}`}
                disabled={confirmTransaction.isPending}
                onClick={() => confirmTransaction.mutate(transaction)}
              >
                {t("row.confirm")}
              </Button>
            ) : null}
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
            <Button asChild variant="outline" size="sm">
              <Link
                href={`/history?entity=Transaction&id=${transaction.id}`}
                aria-label={`${t("row.history")}: ${rowName}`}
              >
                {t("row.history")}
              </Link>
            </Button>
          </>
        )}
      </div>

      {deleteFailure ? (
        <p className="text-xs text-destructive">
          {deleteFailure.status === 409
            ? t("row.deleteLinked")
            : `${t("row.deleteFailed")} ${deleteFailure.message}`}
        </p>
      ) : null}

      {confirmFailure ? (
        <p className="text-xs text-destructive">
          {confirmFailure.status === 409
            ? t("row.confirmStale")
            : `${t("row.confirmFailed")} ${confirmFailure.message}`}
        </p>
      ) : null}
    </li>
  );
}
