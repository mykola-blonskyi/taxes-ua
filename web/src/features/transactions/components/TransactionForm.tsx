"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import {
  useClients,
  useCreateTransaction,
  useUpdateTransaction,
  type TransactionKind,
  type TransactionRequest,
  type TransactionResponse,
} from "@/data/transactions/useTransactions";
import { formatMoney, parseHryvnia } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";
import { SelectField, TextField } from "@/shared/ui/fields";
import { isNonIncomeKind, kindOptions } from "../kinds";

function todayInKyiv(): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Kyiv" }).format(new Date());
}

function kopecksToAmountText(kopecks: number): string {
  const digits = String(kopecks).padStart(3, "0");

  return `${digits.slice(0, -2)},${digits.slice(-2)}`;
}

type FormState = {
  valueDate: string;
  amountText: string;
  kind: TransactionKind;
  nonIncomeReason: string;
  clientName: string;
  invoiceNumber: string;
  description: string;
};

function emptyForm(): FormState {
  return {
    valueDate: todayInKyiv(),
    amountText: "",
    kind: "Income",
    nonIncomeReason: "",
    clientName: "",
    invoiceNumber: "",
    description: "",
  };
}

function toFormState(transaction: TransactionResponse): FormState {
  return {
    valueDate: transaction.valueDate,
    amountText: kopecksToAmountText(Number(transaction.amountMinor)),
    kind: transaction.kind,
    nonIncomeReason: transaction.nonIncomeReason ?? "",
    clientName: transaction.clientName ?? "",
    invoiceNumber: transaction.invoiceNumber ?? "",
    description: transaction.description ?? "",
  };
}

function orNull(value: string): string | null {
  const trimmed = value.trim();

  return trimmed === "" ? null : trimmed;
}

export function TransactionForm({
  editing,
  onUpdated,
  onCancel,
}: {
  editing: TransactionResponse | null;
  onUpdated: () => void;
  onCancel: () => void;
}) {
  const t = useTranslations("transactions.form");
  const tKinds = useTranslations("transactions.kinds");
  const locale = useLocale();
  const { data: clients } = useClients();
  const createTransaction = useCreateTransaction();
  const updateTransaction = useUpdateTransaction();

  // The parent remounts this component (via `key`) whenever `editing` changes, so this initializer
  // is the only place the form state needs to react to it.
  const [form, setForm] = useState<FormState>(() => (editing ? toFormState(editing) : emptyForm()));

  const mutation = editing ? updateTransaction : createTransaction;
  const failure = mutation.error instanceof ApiError ? mutation.error : null;
  const fieldErrors = failure?.errors;
  const rejectedFields = Object.keys(fieldErrors ?? {}).length > 0;

  const parsedAmount = parseHryvnia(form.amountText);
  const amountIsInvalid = form.amountText.trim() !== "" && parsedAmount === null;
  const nonIncome = isNonIncomeKind(form.kind);

  function submit() {
    if (parsedAmount === null) {
      return;
    }

    const body: TransactionRequest = {
      valueDate: form.valueDate,
      amountMinor: parsedAmount,
      kind: form.kind,
      nonIncomeReason: nonIncome ? orNull(form.nonIncomeReason) : null,
      clientName: orNull(form.clientName),
      invoiceNumber: orNull(form.invoiceNumber),
      description: orNull(form.description),
    };

    if (editing) {
      updateTransaction.mutate(
        { id: editing.id, body },
        { onSuccess: onUpdated },
      );

      return;
    }

    createTransaction.mutate(body, {
      onSuccess: () => {
        setForm((current) => ({ ...emptyForm(), valueDate: current.valueDate, kind: current.kind }));
      },
    });
  }

  return (
    <form
      className="flex flex-col gap-4 rounded-lg border p-4"
      onSubmit={(event) => {
        event.preventDefault();
        submit();
      }}
    >
      <h3 className="text-sm font-semibold">{editing ? t("editTitle") : t("newTitle")}</h3>

      {rejectedFields ? (
        <p className="rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">
          {t("validationError")}
        </p>
      ) : null}

      <div className="grid gap-4 sm:grid-cols-2">
        <TextField
          id="transaction-value-date"
          label={t("valueDate")}
          type="date"
          value={form.valueDate}
          onChange={(value) => setForm((current) => ({ ...current, valueDate: value }))}
          errors={fieldErrors?.valueDate}
        />

        <TextField
          id="transaction-amount"
          label={t("amount")}
          inputMode="decimal"
          autoComplete="off"
          value={form.amountText}
          onChange={(value) => setForm((current) => ({ ...current, amountText: value }))}
          hint={parsedAmount !== null ? formatMoney(parsedAmount, locale) : undefined}
          errors={amountIsInvalid ? [t("amountInvalid")] : fieldErrors?.amountMinor}
        />

        <SelectField
          id="transaction-kind"
          label={t("kind")}
          value={form.kind}
          onChange={(value) => setForm((current) => ({ ...current, kind: value as TransactionKind }))}
          options={kindOptions.map((kind) => ({ value: kind, label: tKinds(kind) }))}
          errors={fieldErrors?.kind}
        />

        {nonIncome ? (
          <TextField
            id="transaction-non-income-reason"
            label={`${t("nonIncomeReason")} *`}
            value={form.nonIncomeReason}
            onChange={(value) => setForm((current) => ({ ...current, nonIncomeReason: value }))}
            errors={fieldErrors?.nonIncomeReason}
          />
        ) : null}

        <TextField
          id="transaction-client-name"
          label={t("clientName")}
          list="transaction-clients"
          autoComplete="off"
          value={form.clientName}
          onChange={(value) => setForm((current) => ({ ...current, clientName: value }))}
          errors={fieldErrors?.clientName}
        />
        <datalist id="transaction-clients">
          {(clients ?? []).map((client) => (
            <option key={client} value={client} />
          ))}
        </datalist>

        <TextField
          id="transaction-invoice-number"
          label={t("invoiceNumber")}
          value={form.invoiceNumber}
          onChange={(value) => setForm((current) => ({ ...current, invoiceNumber: value }))}
          errors={fieldErrors?.invoiceNumber}
        />

        <TextField
          id="transaction-description"
          label={t("description")}
          value={form.description}
          onChange={(value) => setForm((current) => ({ ...current, description: value }))}
          errors={fieldErrors?.description}
        />
      </div>

      {failure && !rejectedFields ? (
        <p className="text-sm text-destructive">{`${t("saveFailed")} ${failure.message}`}</p>
      ) : null}

      <div className="flex gap-2">
        <Button type="submit" disabled={mutation.isPending || parsedAmount === null}>
          {mutation.isPending ? t("saving") : editing ? t("save") : t("add")}
        </Button>
        {editing ? (
          <Button type="button" variant="outline" onClick={onCancel}>
            {t("cancel")}
          </Button>
        ) : null}
      </div>
    </form>
  );
}
