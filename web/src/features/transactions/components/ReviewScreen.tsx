"use client";

import { useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { useInvoiceSuggestions, type InvoiceSummary } from "@/data/invoices/useInvoices";
import { useReviewQueue, type TransactionResponse } from "@/data/transactions/useTransactions";
import { TransactionForm } from "./TransactionForm";
import { TransactionTable } from "./TransactionTable";

export function ReviewScreen() {
  const t = useTranslations("transactions.review");
  const [editing, setEditing] = useState<TransactionResponse | null>(null);
  const formRef = useRef<HTMLDivElement>(null);

  const { data, isLoading, isError } = useReviewQueue();
  const { data: offered } = useInvoiceSuggestions();
  // A dismissal only hides the offer for this visit. Confirming the receipt unlinked is what ends it.
  const [dismissed, setDismissed] = useState<ReadonlySet<string>>(new Set());
  const [failures, setFailures] = useState<Readonly<Record<string, string>>>({});
  const suggestions: Record<string, InvoiceSummary[]> = {};
  for (const offer of offered ?? []) {
    if (!dismissed.has(offer.receiptId)) {
      suggestions[offer.receiptId] = offer.invoices;
    }
  }

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
          <TransactionTable
            items={data}
            onEdit={startEdit}
            suggestions={suggestions}
            failures={failures}
            onSuggestionFailure={(id, message) =>
              setFailures((current) => {
                const next = { ...current };
                if (message) {
                  next[id] = message;
                } else {
                  delete next[id];
                }

                return next;
              })
            }
            onDismissSuggestion={(id) => setDismissed((current) => new Set(current).add(id))}
          />
        )
      ) : null}
    </div>
  );
}
