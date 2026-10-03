"use client";

import { useRef, useState } from "react";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { LoadState } from "@/data/api/LoadState";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { useTransactions, type TransactionResponse } from "@/data/transactions/useTransactions";
import { currentYearInKyiv } from "@/shared/lib/dates";
import { formatMoney } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";
import { ExportButtons } from "./ExportButtons";
import { TransactionForm } from "./TransactionForm";
import { TransactionTable } from "./TransactionTable";

export function TransactionsScreen() {
  const t = useTranslations("transactions");
  const locale = useLocale();
  const [year, setYear] = useState(() => currentYearInKyiv());
  const [editing, setEditing] = useState<TransactionResponse | null>(null);
  const formRef = useRef<HTMLDivElement>(null);

  const query = useTransactions(year);
  const { data } = query;

  function startEdit(transaction: TransactionResponse) {
    setEditing(transaction);
    formRef.current?.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <Button
            type="button"
            variant="outline"
            size="icon-sm"
            aria-label={t("year.previous")}
            onClick={() => setYear((current) => current - 1)}
          >
            <ChevronLeft aria-hidden="true" />
          </Button>
          <span className="min-w-10 text-center text-base font-semibold">{year}</span>
          <Button
            type="button"
            variant="outline"
            size="icon-sm"
            aria-label={t("year.next")}
            onClick={() => setYear((current) => current + 1)}
          >
            <ChevronRight aria-hidden="true" />
          </Button>
        </div>
        <Link href="/invoices" className="text-sm font-medium text-primary underline-offset-4 hover:underline pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center">
          {t("invoicesLink")}
        </Link>
      </div>

      {query.isLoading || query.isError ? (
        <LoadState query={query} loading={t("loading")} failed={t("loadFailed")} resetKey={year} />
      ) : null}

      {data ? (
        <>
          <div className="flex flex-col gap-1 rounded-lg border bg-muted p-4">
            <span className="text-sm text-muted-foreground">{t("summary.title", { year })}</span>
            <span className="text-xl font-semibold">{formatMoney(Number(data.totalIncomeKop), locale)}</span>
            <span className="text-xs text-muted-foreground">{t("summary.hint")}</span>
          </div>

          {data.items.length > 0 ? <ExportButtons year={year} /> : null}

          {data.fopRegistrationDate === null ? (
            <section className="flex flex-col gap-2 rounded-lg border bg-muted p-4">
              <h3 className="text-sm font-semibold text-destructive">{t("registrationWarning.title")}</h3>
              <p className="text-sm text-muted-foreground">{t("registrationWarning.message")}</p>
              <Link
                href="/settings"
                className="text-sm font-medium text-primary underline-offset-4 hover:underline pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center"
              >
                {t("registrationWarning.cta")}
              </Link>
            </section>
          ) : null}

          <div ref={formRef}>
            <TransactionForm
              key={editing?.id ?? "new"}
              editing={editing}
              onUpdated={() => setEditing(null)}
              onCancel={() => setEditing(null)}
            />
          </div>

          {data.items.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t("empty")}</p>
          ) : (
            <TransactionTable items={data.items} onEdit={startEdit} />
          )}
        </>
      ) : null}
    </div>
  );
}
