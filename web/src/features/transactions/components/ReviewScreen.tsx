"use client";

import { useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { useReviewQueue, type TransactionResponse } from "@/data/transactions/useTransactions";
import { TransactionForm } from "./TransactionForm";
import { TransactionTable } from "./TransactionTable";

export function ReviewScreen() {
  const t = useTranslations("transactions.review");
  const [editing, setEditing] = useState<TransactionResponse | null>(null);
  const formRef = useRef<HTMLDivElement>(null);

  const { data, isLoading, isError } = useReviewQueue();

  function startEdit(transaction: TransactionResponse) {
    setEditing(transaction);
    formRef.current?.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">{t("intro")}</p>

      {isLoading ? <p className="text-sm text-muted-foreground">{t("loading")}</p> : null}
      {isError ? <p className="text-sm text-destructive">{t("loadFailed")}</p> : null}

      <div ref={formRef}>
        {editing ? (
          <TransactionForm
            key={editing.id}
            editing={editing}
            onUpdated={() => setEditing(null)}
            onCancel={() => setEditing(null)}
          />
        ) : null}
      </div>

      {data ? (
        data.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("empty")}</p>
        ) : (
          <TransactionTable items={data} onEdit={startEdit} />
        )
      ) : null}
    </div>
  );
}
