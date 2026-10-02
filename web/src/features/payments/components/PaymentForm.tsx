"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import {
  paymentKinds,
  useCreatePayment,
  useUpdatePayment,
  type PaymentKind,
  type PaymentRequest,
  type PaymentResponse,
} from "@/data/payments/usePayments";
import { todayInKyiv } from "@/shared/lib/dates";
import { formatMoney, parseHryvnia } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";
import { SelectField, TextField } from "@/shared/ui/fields";
import { fromPeriodValue, toPeriodValue, type PeriodValue } from "../period";
import { PeriodSelect } from "./PeriodSelect";

type FormState = {
  paidOn: string;
  kind: PaymentKind;
  amountText: string;
  periodYear: number;
  period: PeriodValue;
  note: string;
};

function kopecksToAmountText(kopecks: number): string {
  const digits = String(kopecks).padStart(3, "0");

  return `${digits.slice(0, -2)},${digits.slice(-2)}`;
}

function emptyForm(year: number): FormState {
  return { paidOn: todayInKyiv(), kind: "Esv", amountText: "", periodYear: year, period: "q1", note: "" };
}

function toFormState(payment: PaymentResponse): FormState {
  return {
    paidOn: payment.paidOn,
    kind: payment.kind,
    amountText: kopecksToAmountText(Number(payment.amountKop)),
    periodYear: Number(payment.periodYear),
    period: toPeriodValue(payment),
    note: payment.note ?? "",
  };
}

export function PaymentForm({
  year,
  configuredYears,
  editing,
  onDone,
}: {
  year: number;
  configuredYears: number[];
  editing: PaymentResponse | null;
  onDone: () => void;
}) {
  const t = useTranslations("payments");
  const tForm = useTranslations("payments.form");
  const locale = useLocale();
  const createPayment = useCreatePayment();
  const updatePayment = useUpdatePayment();

  // The parent remounts this form (via `key`) when `editing` or the year changes.
  const [form, setForm] = useState<FormState>(() => (editing ? toFormState(editing) : emptyForm(year)));

  const mutation = editing ? updatePayment : createPayment;
  const failure = mutation.error instanceof ApiError ? mutation.error : null;
  const fieldErrors = failure?.errors;
  const rejectedFields = Object.keys(fieldErrors ?? {}).length > 0;

  const parsedAmount = parseHryvnia(form.amountText);
  const amountIsInvalid = form.amountText.trim() !== "" && (parsedAmount === null || parsedAmount === 0);
  const yearOptions = configuredYears.includes(form.periodYear)
    ? configuredYears
    : [...configuredYears, form.periodYear].sort((a, b) => a - b);

  function submit() {
    if (parsedAmount === null || parsedAmount === 0) {
      return;
    }

    const note = form.note.trim();
    const body: PaymentRequest = {
      paidOn: form.paidOn,
      kind: form.kind,
      amountKop: parsedAmount,
      periodYear: form.periodYear,
      ...fromPeriodValue(form.period),
      note: note === "" ? null : note,
    };

    if (editing) {
      updatePayment.mutate({ id: editing.id, body }, { onSuccess: onDone });

      return;
    }

    createPayment.mutate(body, {
      onSuccess: () => setForm((current) => ({ ...emptyForm(year), paidOn: current.paidOn, kind: current.kind })),
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
      <h3 className="text-sm font-semibold">{editing ? tForm("editTitle") : tForm("newTitle")}</h3>

      {rejectedFields ? (
        <p className="rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">
          {tForm("validationError")}
        </p>
      ) : null}

      <div className="grid gap-4 sm:grid-cols-2">
        <SelectField
          id="payment-kind"
          label={tForm("kind")}
          value={form.kind}
          onChange={(value) => setForm((current) => ({ ...current, kind: value as PaymentKind }))}
          options={paymentKinds.map((kind) => ({ value: kind, label: t(`kinds.${kind}`) }))}
          errors={fieldErrors?.kind}
        />

        <TextField
          id="payment-amount"
          label={tForm("amount")}
          inputMode="decimal"
          autoComplete="off"
          value={form.amountText}
          onChange={(value) => setForm((current) => ({ ...current, amountText: value }))}
          hint={parsedAmount ? formatMoney(parsedAmount, locale) : undefined}
          errors={amountIsInvalid ? [tForm("amountInvalid")] : fieldErrors?.amountKop}
        />

        <TextField
          id="payment-paid-on"
          label={tForm("paidOn")}
          type="date"
          value={form.paidOn}
          onChange={(value) => setForm((current) => ({ ...current, paidOn: value }))}
          errors={fieldErrors?.paidOn}
        />

        <SelectField
          id="payment-period-year"
          label={tForm("periodYear")}
          value={String(form.periodYear)}
          onChange={(value) => setForm((current) => ({ ...current, periodYear: Number(value) }))}
          options={yearOptions.map((option) => ({ value: String(option), label: String(option) }))}
          errors={fieldErrors?.periodYear}
        />

        <PeriodSelect
          id="payment-period"
          year={form.periodYear}
          kind={form.kind}
          value={form.period}
          onChange={(period) => setForm((current) => ({ ...current, period }))}
          hint={tForm("periodHint")}
          errors={fieldErrors?.periodQuarter ?? fieldErrors?.periodMonth}
        />

        <TextField
          id="payment-note"
          label={tForm("note")}
          value={form.note}
          onChange={(value) => setForm((current) => ({ ...current, note: value }))}
          errors={fieldErrors?.note}
        />
      </div>

      {failure && !rejectedFields ? (
        <p className="text-sm text-destructive">{`${tForm("saveFailed")} ${failure.message}`}</p>
      ) : null}

      <div className="flex gap-2">
        <Button type="submit" disabled={mutation.isPending || !parsedAmount}>
          {mutation.isPending ? tForm("saving") : editing ? tForm("save") : tForm("add")}
        </Button>
        {editing ? (
          <Button type="button" variant="outline" onClick={onDone}>
            {tForm("cancel")}
          </Button>
        ) : null}
      </div>
    </form>
  );
}
