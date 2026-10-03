"use client";

import { useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import { useApiErrorText } from "@/data/api/useApiErrorText";
import {
  useInvoicingDetails,
  useSaveInvoicingDetails,
  type InvoicingDetailsResponse,
  type MonobankPrefillResponse,
} from "@/data/invoicing/useInvoicing";
import { useMonobankConnection } from "@/data/monobank/useMonobank";
import { Button } from "@/shared/ui/button";
import { LoadState } from "@/data/api/LoadState";
import { TextAreaField, TextField, FieldForm } from "@/shared/ui/fields";
import { InvoicingMonobankPrefill } from "./InvoicingMonobankPrefill";
import { InvoicingSignature } from "./InvoicingSignature";
import {
  currencies,
  sentCurrencies,
  toFormState,
  toRequest,
  type Currency,
  type FormState,
  type PaymentForm,
} from "./invoicingFormState";

type ClauseKey = "acceptance" | "fees" | "taxStatus";

const clauses = ["acceptance", "fees", "taxStatus"] as const satisfies readonly ClauseKey[];

export function InvoicingForm() {
  const t = useTranslations("settings");
  const query = useInvoicingDetails();
  const { data } = query;

  if (query.isError || !data) {
    return <LoadState query={query} loading={t("loading")} failed={t("loadFailed")} />;
  }

  return <InvoicingFormBody details={data} />;
}

function InvoicingFormBody({ details }: { details: InvoicingDetailsResponse }) {
  const t = useTranslations("settings");
  const tInvoicing = useTranslations("settings.invoicing");
  const apiText = useApiErrorText();
  const save = useSaveInvoicingDetails();
  const connection = useMonobankConnection();
  const [form, setForm] = useState<FormState>(() => toFormState(details));

  const failure = save.error instanceof ApiError ? save.error : null;
  const rejected = Object.keys(failure?.fieldCodes ?? {}).length > 0;
  const sent = sentCurrencies(form);

  function fieldErrors(key: string): string[] | undefined {
    return failure?.fieldCodes[key]?.map(apiText.ofCode);
  }

  function paymentErrors(currency: Currency, field: keyof PaymentForm): string[] | undefined {
    const index = sent.indexOf(currency);

    return index < 0 ? undefined : fieldErrors(`paymentDetails[${index}].${field}`);
  }

  const setField = (field: keyof Omit<FormState, "payments">) => (value: string) =>
    setForm((current) => ({ ...current, [field]: value }));

  const setPayment = (currency: Currency, field: keyof PaymentForm) => (value: string) =>
    setForm((current) => ({
      ...current,
      payments: { ...current.payments, [currency]: { ...current.payments[currency], [field]: value } },
    }));

  function applyPrefill(prefill: MonobankPrefillResponse) {
    setForm((current) => {
      const payments = { ...current.payments };
      for (const { currency, iban, beneficiaryBank, swift } of prefill.paymentDetails) {
        payments[currency] = { ...payments[currency], iban, beneficiaryBank, swift };
      }

      return {
        ...current,
        sellerNameUk: prefill.sellerNameUk === "" ? current.sellerNameUk : prefill.sellerNameUk,
        payments,
      };
    });
  }

  function resetClause(clause: ClauseKey) {
    const en = `${clause}ClauseEn` as const;
    const uk = `${clause}ClauseUk` as const;
    setForm((current) => ({ ...current, [en]: details.defaults[en], [uk]: details.defaults[uk] }));
  }

  return (
    <FieldForm quietErrors={Boolean(rejected)}
      className="flex min-w-0 flex-col gap-6"
      onSubmit={(event) => {
        event.preventDefault();
        save.mutate(toRequest(form));
      }}
    >
      {rejected ? (
        <p role="alert" className="rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">
          {t("validationError")}
        </p>
      ) : null}

      <fieldset className="flex min-w-0 flex-col gap-4">
        <legend className="text-base font-semibold">{tInvoicing("seller")}</legend>
        <div className="grid min-w-0 gap-4 sm:grid-cols-2">
          <TextField
            id="seller-name-uk"
            label={tInvoicing("sellerNameUk")}
            value={form.sellerNameUk}
            onChange={setField("sellerNameUk")}
            errors={fieldErrors("sellerNameUk")}
          />
          <TextField
            id="seller-name-en"
            label={tInvoicing("sellerNameEn")}
            value={form.sellerNameEn}
            onChange={setField("sellerNameEn")}
            errors={fieldErrors("sellerNameEn")}
          />
          <TextField
            id="rnokpp"
            label={tInvoicing("rnokpp")}
            inputMode="numeric"
            maxLength={10}
            value={form.rnokpp}
            onChange={setField("rnokpp")}
            errors={fieldErrors("rnokpp")}
          />
        </div>
        <div className="grid min-w-0 gap-4 sm:grid-cols-2">
          <TextAreaField
            id="address-uk"
            label={tInvoicing("addressUk")}
            rows={2}
            value={form.addressUk}
            onChange={setField("addressUk")}
            errors={fieldErrors("addressUk")}
          />
          <TextAreaField
            id="address-en"
            label={tInvoicing("addressEn")}
            rows={2}
            value={form.addressEn}
            onChange={setField("addressEn")}
            errors={fieldErrors("addressEn")}
          />
        </div>
      </fieldset>

      <section className="flex min-w-0 flex-col gap-4" aria-labelledby="payment-details-heading">
        <div className="flex flex-col gap-1">
          <h3 id="payment-details-heading" className="text-base font-semibold">
            {tInvoicing("payment")}
          </h3>
          <p className="text-xs text-muted-foreground">{tInvoicing("paymentHint")}</p>
        </div>
        {connection.data?.connected ? (
          <InvoicingMonobankPrefill form={form} onApply={applyPrefill} />
        ) : null}
        {currencies.map((currency) => (
          <fieldset key={currency} className="flex min-w-0 flex-col gap-3 rounded-lg border p-3">
            <legend className="px-1 text-sm font-semibold">{currency}</legend>
            <TextField
              id={`iban-${currency}`}
              label={tInvoicing("iban")}
              autoCapitalize="characters"
              value={form.payments[currency].iban}
              onChange={setPayment(currency, "iban")}
              errors={paymentErrors(currency, "iban")}
            />
            <TextField
              id={`beneficiary-bank-${currency}`}
              label={tInvoicing("beneficiaryBank")}
              value={form.payments[currency].beneficiaryBank}
              onChange={setPayment(currency, "beneficiaryBank")}
              errors={paymentErrors(currency, "beneficiaryBank")}
            />
            <TextField
              id={`swift-${currency}`}
              label={tInvoicing("swift")}
              autoCapitalize="characters"
              value={form.payments[currency].swift}
              onChange={setPayment(currency, "swift")}
              errors={paymentErrors(currency, "swift")}
            />
            <TextField
              id={`intermediary-bank-${currency}`}
              label={tInvoicing("intermediaryBank")}
              hint={tInvoicing("intermediaryHint")}
              value={form.payments[currency].intermediaryBank}
              onChange={setPayment(currency, "intermediaryBank")}
              errors={paymentErrors(currency, "intermediaryBank")}
            />
            <TextField
              id={`intermediary-swift-${currency}`}
              label={tInvoicing("intermediarySwift")}
              value={form.payments[currency].intermediarySwift}
              onChange={setPayment(currency, "intermediarySwift")}
              errors={paymentErrors(currency, "intermediarySwift")}
            />
            <TextField
              id={`intermediary-account-${currency}`}
              label={tInvoicing("intermediaryAccount")}
              value={form.payments[currency].intermediaryAccount}
              onChange={setPayment(currency, "intermediaryAccount")}
              errors={paymentErrors(currency, "intermediaryAccount")}
            />
          </fieldset>
        ))}
      </section>

      <section className="flex min-w-0 flex-col gap-4" aria-labelledby="clauses-heading">
        <div className="flex flex-col gap-1">
          <h3 id="clauses-heading" className="text-base font-semibold">
            {tInvoicing("clauses")}
          </h3>
          <p className="text-xs text-muted-foreground">{tInvoicing("clausesHint")}</p>
        </div>
        {clauses.map((clause) => {
          const en = `${clause}ClauseEn` as const;
          const uk = `${clause}ClauseUk` as const;
          const isDefault = form[en] === details.defaults[en] && form[uk] === details.defaults[uk];

          return (
            <fieldset key={clause} className="flex min-w-0 flex-col gap-3 rounded-lg border p-3">
              <legend className="px-1 text-sm font-semibold">{tInvoicing(`clause.${clause}`)}</legend>
              <TextAreaField
                id={`${clause}-en`}
                label={tInvoicing("english")}
                rows={3}
                value={form[en]}
                onChange={setField(en)}
                errors={fieldErrors(en)}
              />
              <TextAreaField
                id={`${clause}-uk`}
                label={tInvoicing("ukrainian")}
                rows={3}
                value={form[uk]}
                onChange={setField(uk)}
                errors={fieldErrors(uk)}
              />
              <div className="flex items-center gap-3">
                <Button type="button" variant="outline" size="sm" disabled={isDefault} onClick={() => resetClause(clause)}>
                  {tInvoicing("resetClause")}
                </Button>
                {isDefault ? <span className="text-xs text-muted-foreground">{tInvoicing("defaultWording")}</span> : null}
              </div>
            </fieldset>
          );
        })}
      </section>

      <InvoicingSignature details={details} />

      {failure && !rejected ? (
        <p className="text-sm text-destructive">{apiText.withReason(t("saveFailed"), failure)}</p>
      ) : null}
      {save.isSuccess ? <p className="text-sm text-muted-foreground">{tInvoicing("saved")}</p> : null}

      <div className="flex flex-wrap items-center gap-2">
        <Button type="submit" disabled={save.isPending}>
          {save.isPending ? t("saving") : t("save")}
        </Button>
        <Button asChild variant="outline">
          <Link href="/history?entity=InvoicingDetails">{t("fop.history")}</Link>
        </Button>
      </div>
    </FieldForm>
  );
}
