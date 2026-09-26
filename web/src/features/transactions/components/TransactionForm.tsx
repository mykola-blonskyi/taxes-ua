"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import { currencies, useFxRate, type Currency } from "@/data/fx/useFxRate";
import {
  useClients,
  useCreateTransaction,
  useUpdateTransaction,
  type TransactionKind,
  type TransactionRequest,
  type TransactionResponse,
} from "@/data/transactions/useTransactions";
import { formatMinor, formatMoney, parseHryvnia, parseRate, toUahKop } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";
import { SelectField, TextField } from "@/shared/ui/fields";
import { formatNumericDate } from "../dates";
import { isNonIncomeKind, kindOptions } from "../kinds";

function todayInKyiv(): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Kyiv" }).format(new Date());
}

function kopecksToAmountText(kopecks: number): string {
  const digits = String(kopecks).padStart(3, "0");

  return `${digits.slice(0, -2)},${digits.slice(-2)}`;
}

function rateE4ToText(rateE4: number): string {
  const digits = String(rateE4).padStart(5, "0");

  return `${digits.slice(0, -4)},${digits.slice(-4)}`;
}

// "nbu" means the server resolves the rate on save; the form only shows it.
type RateState = { mode: "nbu" } | { mode: "manual"; text: string };

type FormState = {
  valueDate: string;
  amountText: string;
  currency: Currency;
  rate: RateState;
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
    currency: "UAH",
    rate: { mode: "nbu" },
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
    currency: transaction.currency,
    rate:
      transaction.rateSource === "Manual"
        ? { mode: "manual", text: rateE4ToText(Number(transaction.rateE4)) }
        : { mode: "nbu" },
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
  const tCurrencies = useTranslations("transactions.currencies");
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
  const foreign = form.currency !== "UAH";

  // The server keeps a stored NBU rate on PUT while currency and valueDate stay the same, so the
  // form shows that rate instead of asking NBU again.
  const storedNbuRate =
    editing &&
    editing.rateSource === "Nbu" &&
    editing.rateDate !== null &&
    editing.currency === form.currency &&
    editing.valueDate === form.valueDate
      ? { rateE4: Number(editing.rateE4), rateDate: editing.rateDate }
      : null;

  const fx = useFxRate(form.currency, form.valueDate, form.rate.mode === "nbu" && storedNbuRate === null);
  const nbuRate = storedNbuRate ?? (fx.data ? { rateE4: Number(fx.data.rateE4), rateDate: fx.data.rateDate } : null);
  const nbuUnavailable = storedNbuRate === null && fx.isError;

  const manualRateE4 = form.rate.mode === "manual" ? parseRate(form.rate.text) : null;
  const rateIsInvalid = form.rate.mode === "manual" && form.rate.text.trim() !== "" && manualRateE4 === null;
  const effectiveRateE4 = !foreign ? null : form.rate.mode === "manual" ? manualRateE4 : (nbuRate?.rateE4 ?? null);
  const missingRate = foreign && effectiveRateE4 === null;

  // In nbu mode the field shows the NBU rate, or stays empty while it loads or when NBU is down;
  // typing into it is what switches to a manual rate.
  const rateText = form.rate.mode === "manual" ? form.rate.text : nbuRate ? rateE4ToText(nbuRate.rateE4) : "";

  function rateHint(): string | undefined {
    if (form.rate.mode === "manual") {
      return t("rateManual");
    }

    if (nbuRate) {
      return nbuRate.rateDate === form.valueDate
        ? t("rateNbuFor", { date: formatNumericDate(nbuRate.rateDate, locale) })
        : t("rateNbuFallback", { date: formatNumericDate(nbuRate.rateDate, locale, true) });
    }

    return fx.isFetching ? t("rateLoading") : undefined;
  }

  function rateErrors(): string[] | undefined {
    if (rateIsInvalid) {
      return [t("rateInvalid")];
    }

    if (form.rate.mode === "nbu" && nbuUnavailable) {
      return [t("nbuUnavailable")];
    }

    return fieldErrors?.manualRateE4;
  }

  function submit() {
    if (parsedAmount === null || missingRate) {
      return;
    }

    const body: TransactionRequest = {
      valueDate: form.valueDate,
      amountMinor: parsedAmount,
      currency: form.currency,
      manualRateE4: foreign ? manualRateE4 : null,
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
        setForm((current) => ({
          ...emptyForm(),
          valueDate: current.valueDate,
          kind: current.kind,
          currency: current.currency,
        }));
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
          hint={
            parsedAmount === null
              ? undefined
              : foreign
                ? formatMinor(parsedAmount, form.currency, locale)
                : formatMoney(parsedAmount, locale)
          }
          errors={amountIsInvalid ? [t("amountInvalid")] : fieldErrors?.amountMinor}
        />

        <SelectField
          id="transaction-currency"
          label={t("currency")}
          value={form.currency}
          onChange={(value) =>
            setForm((current) => ({ ...current, currency: value as Currency, rate: { mode: "nbu" } }))
          }
          options={currencies.map((currency) => ({ value: currency, label: tCurrencies(currency) }))}
          errors={fieldErrors?.currency}
        />

        {foreign ? (
          <div className="flex min-w-0 flex-col gap-1">
            <TextField
              id="transaction-rate"
              label={t("rate")}
              inputMode="decimal"
              autoComplete="off"
              value={rateText}
              onChange={(value) => setForm((current) => ({ ...current, rate: { mode: "manual", text: value } }))}
              hint={rateHint()}
              errors={rateErrors()}
            />
            {form.rate.mode === "manual" ? (
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="self-start"
                onClick={() => setForm((current) => ({ ...current, rate: { mode: "nbu" } }))}
              >
                {t("useNbuRate")}
              </Button>
            ) : null}
            {parsedAmount !== null && effectiveRateE4 !== null ? (
              <p className="min-w-0 break-words text-sm">
                {`${t("uahEquivalent")}: ≈ ${formatMoney(toUahKop(parsedAmount, effectiveRateE4), locale)}`}
              </p>
            ) : null}
          </div>
        ) : null}

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
        <Button type="submit" disabled={mutation.isPending || parsedAmount === null || missingRate}>
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
