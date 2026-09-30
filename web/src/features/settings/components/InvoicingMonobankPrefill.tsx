"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import { usePrefillFromMonobank, type MonobankPrefillResponse } from "@/data/invoicing/useInvoicing";
import { Button } from "@/shared/ui/button";
import type { FormState } from "./invoicingFormState";

// The suggestions are shown, not applied: a value the owner already saved changes only when they accept it
// here, and reaches the database only when they save the form.
export function InvoicingMonobankPrefill({
  form,
  onApply,
}: {
  form: FormState;
  onApply: (prefill: MonobankPrefillResponse) => void;
}) {
  const t = useTranslations("settings.invoicing.prefill");
  const prefill = usePrefillFromMonobank();
  const [suggestion, setSuggestion] = useState<MonobankPrefillResponse | null>(null);

  const failure = prefill.error instanceof ApiError ? prefill.error : null;
  const changes = suggestion ? changesFor(suggestion, form, t("name")) : [];

  return (
    <div className="flex min-w-0 flex-col gap-2 rounded-lg border border-dashed p-3">
      <p className="text-sm">{t("intro")}</p>
      <div>
        <Button
          type="button"
          variant="outline"
          disabled={prefill.isPending}
          onClick={() => prefill.mutate(undefined, { onSuccess: (data) => setSuggestion(data ?? null) })}
        >
          {prefill.isPending ? t("loading") : t("button")}
        </Button>
      </div>
      {failure ? (
        <p role="alert" className="text-sm text-destructive">
          {failure.status === 409 ? t("notConnected") : t("failed")}
        </p>
      ) : null}
      {suggestion ? (
        changes.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("nothingToChange")}</p>
        ) : (
          <div className="flex min-w-0 flex-col gap-2">
            <p className="text-sm font-medium">{t("suggestions")}</p>
            <ul className="flex min-w-0 flex-col gap-1 text-sm">
              {changes.map((change) => (
                <li key={change.label} className="min-w-0 break-words">
                  <span className="font-medium">{change.label}:</span> {change.value}
                </li>
              ))}
            </ul>
            <p className="text-xs text-muted-foreground">{t("notSavedYet")}</p>
            <div className="flex flex-wrap gap-2">
              <Button
                type="button"
                size="sm"
                onClick={() => {
                  onApply(suggestion);
                  setSuggestion(null);
                }}
              >
                {t("apply")}
              </Button>
              <Button type="button" size="sm" variant="outline" onClick={() => setSuggestion(null)}>
                {t("dismiss")}
              </Button>
            </div>
          </div>
        )
      ) : null}
    </div>
  );
}

function changesFor(suggestion: MonobankPrefillResponse, form: FormState, nameLabel: string) {
  const changes: { label: string; value: string }[] = [];
  if (suggestion.sellerNameUk !== "" && suggestion.sellerNameUk !== form.sellerNameUk) {
    changes.push({ label: nameLabel, value: suggestion.sellerNameUk });
  }

  for (const { currency, iban, beneficiaryBank, swift } of suggestion.paymentDetails) {
    const current = form.payments[currency];
    if (current.iban !== iban || current.beneficiaryBank !== beneficiaryBank || current.swift !== swift) {
      changes.push({ label: currency, value: `${iban}, ${beneficiaryBank}, ${swift}` });
    }
  }

  return changes;
}
