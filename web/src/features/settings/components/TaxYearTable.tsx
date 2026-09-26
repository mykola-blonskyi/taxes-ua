"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import {
  useCloneTaxYear,
  useSaveTaxYear,
  useTaxYears,
  useVerifyTaxYear,
  type TaxYearConfigRequest,
  type TaxYearConfigResponse,
} from "@/data/tax-years/useTaxYears";
import { Button } from "@/shared/ui/button";
import { MoneyField, NumberField, RateField, ReadOnlyMoneyField, TextField } from "./fields";

type FormState = {
  minWageKop: number;
  singleTaxRateBp: number;
  militaryLevyRateBp: number;
  esvRateBp: number;
  excessRateBp: number;
  incomeLimitMinWages: number;
  limitWarnThresholdsPctText: string;
  esvDeadlineDay: number;
  declarationDays: number;
  taxPaymentDaysAfterDeclaration: number;
  advanceRecommendedDay: number;
  holidaysText: string;
  source: string;
};

function toFormState(taxYear: TaxYearConfigResponse): FormState {
  return {
    minWageKop: Number(taxYear.minWageKop),
    singleTaxRateBp: Number(taxYear.singleTaxRateBp),
    militaryLevyRateBp: Number(taxYear.militaryLevyRateBp),
    esvRateBp: Number(taxYear.esvRateBp),
    excessRateBp: Number(taxYear.excessRateBp),
    incomeLimitMinWages: Number(taxYear.incomeLimitMinWages),
    limitWarnThresholdsPctText: taxYear.limitWarnThresholdsPct.map((value) => String(value)).join(", "),
    esvDeadlineDay: Number(taxYear.esvDeadlineDay),
    declarationDays: Number(taxYear.declarationDays),
    taxPaymentDaysAfterDeclaration: Number(taxYear.taxPaymentDaysAfterDeclaration),
    advanceRecommendedDay: Number(taxYear.advanceRecommendedDay),
    holidaysText: taxYear.holidays.join(", "),
    source: taxYear.source,
  };
}

function toRequest(form: FormState): TaxYearConfigRequest {
  return {
    minWageKop: form.minWageKop,
    singleTaxRateBp: form.singleTaxRateBp,
    militaryLevyRateBp: form.militaryLevyRateBp,
    esvRateBp: form.esvRateBp,
    excessRateBp: form.excessRateBp,
    incomeLimitMinWages: form.incomeLimitMinWages,
    limitWarnThresholdsPct: form.limitWarnThresholdsPctText
      .split(",")
      .map((value) => value.trim())
      .filter((value) => value.length > 0)
      .map(Number),
    esvDeadlineDay: form.esvDeadlineDay,
    declarationDays: form.declarationDays,
    taxPaymentDaysAfterDeclaration: form.taxPaymentDaysAfterDeclaration,
    advanceRecommendedDay: form.advanceRecommendedDay,
    holidays: form.holidaysText
      .split(",")
      .map((value) => value.trim())
      .filter((value) => value.length > 0),
    source: form.source,
  };
}

export function TaxYearTable() {
  const t = useTranslations("settings");
  const tYears = useTranslations("settings.taxYears");
  const { data, isLoading, isError } = useTaxYears();

  if (isLoading) {
    return <p className="text-sm text-muted-foreground">{t("loading")}</p>;
  }

  if (isError || !data) {
    return <p className="text-sm text-destructive">{t("loadFailed")}</p>;
  }

  if (data.length === 0) {
    return <p className="text-sm text-muted-foreground">{tYears("empty")}</p>;
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full border-collapse text-sm">
        <thead>
          <tr className="border-b text-left align-bottom text-xs text-muted-foreground">
            <th className="min-w-24 p-2 font-medium">{tYears("year")}</th>
            <th className="min-w-24 p-2 font-medium">{tYears("minWageKop")}</th>
            <th className="min-w-24 p-2 font-medium">{tYears("singleTaxRateBp")}</th>
            <th className="min-w-24 p-2 font-medium">{tYears("militaryLevyRateBp")}</th>
            <th className="min-w-24 p-2 font-medium">{tYears("esvRateBp")}</th>
            <th className="min-w-24 p-2 font-medium">{tYears("excessRateBp")}</th>
            <th className="min-w-24 p-2 font-medium">{tYears("esvMonthlyKop")}</th>
            <th className="min-w-24 p-2 font-medium">{tYears("incomeLimitMinWages")}</th>
            <th className="min-w-24 p-2 font-medium">{tYears("incomeLimitKop")}</th>
            <th className="min-w-24 p-2 font-medium">
              {tYears("limitWarnThresholdsPct")}
              <span className="block font-normal">{tYears("limitWarnThresholdsPctHint")}</span>
            </th>
            <th className="min-w-24 p-2 font-medium">
              {tYears("esvDeadlineDay")}
              <span className="block font-normal">{tYears("esvDeadlineDayHint")}</span>
            </th>
            <th className="min-w-24 p-2 font-medium">{tYears("declarationDays")}</th>
            <th className="min-w-24 p-2 font-medium">{tYears("taxPaymentDaysAfterDeclaration")}</th>
            <th className="min-w-24 p-2 font-medium">{tYears("advanceRecommendedDay")}</th>
            <th className="min-w-24 p-2 font-medium">
              {tYears("holidays")}
              <span className="block font-normal">{tYears("holidaysHint")}</span>
            </th>
            <th className="min-w-24 p-2 font-medium">{tYears("source")}</th>
            <th className="min-w-24 p-2 font-medium">{tYears("verifiedAt")}</th>
            <th className="p-2 font-medium" />
          </tr>
        </thead>
        <tbody>
          {data.map((taxYear) => (
            <TaxYearRow key={taxYear.year} taxYear={taxYear} />
          ))}
        </tbody>
      </table>
    </div>
  );
}

function TaxYearRow({ taxYear }: { taxYear: TaxYearConfigResponse }) {
  const t = useTranslations("settings");
  const tYears = useTranslations("settings.taxYears");
  const locale = useLocale();
  const year = Number(taxYear.year);
  const next = year + 1;

  const saveTaxYear = useSaveTaxYear();
  const verifyTaxYear = useVerifyTaxYear();
  const cloneTaxYear = useCloneTaxYear();
  const [form, setForm] = useState<FormState>(() => toFormState(taxYear));

  const saveFailure = saveTaxYear.error instanceof ApiError ? saveTaxYear.error : null;
  const fieldErrors = saveFailure?.errors;
  const actionFailure = [verifyTaxYear.error, cloneTaxYear.error].find(
    (error) => error instanceof ApiError,
  );

  return (
    <tr className="border-b align-top">
      <td className="p-2">{year}</td>
      <td className="p-2">
        <MoneyField
          id={`min-wage-${year}`}
          label={`${tYears("minWageKop")} ${year}`}
          labelClassName="sr-only"
          locale={locale}
          valueKop={form.minWageKop}
          onChange={(value) => setForm((current) => ({ ...current, minWageKop: value }))}
          errors={fieldErrors?.minWageKop}
        />
      </td>
      <td className="p-2">
        <RateField
          id={`single-tax-rate-${year}`}
          label={`${tYears("singleTaxRateBp")} ${year}`}
          labelClassName="sr-only"
          locale={locale}
          valueBp={form.singleTaxRateBp}
          onChange={(value) => setForm((current) => ({ ...current, singleTaxRateBp: value }))}
          errors={fieldErrors?.singleTaxRateBp}
        />
      </td>
      <td className="p-2">
        <RateField
          id={`military-levy-rate-${year}`}
          label={`${tYears("militaryLevyRateBp")} ${year}`}
          labelClassName="sr-only"
          locale={locale}
          valueBp={form.militaryLevyRateBp}
          onChange={(value) => setForm((current) => ({ ...current, militaryLevyRateBp: value }))}
          errors={fieldErrors?.militaryLevyRateBp}
        />
      </td>
      <td className="p-2">
        <RateField
          id={`esv-rate-${year}`}
          label={`${tYears("esvRateBp")} ${year}`}
          labelClassName="sr-only"
          locale={locale}
          valueBp={form.esvRateBp}
          onChange={(value) => setForm((current) => ({ ...current, esvRateBp: value }))}
          errors={fieldErrors?.esvRateBp}
        />
      </td>
      <td className="p-2">
        <RateField
          id={`excess-rate-${year}`}
          label={`${tYears("excessRateBp")} ${year}`}
          labelClassName="sr-only"
          locale={locale}
          valueBp={form.excessRateBp}
          onChange={(value) => setForm((current) => ({ ...current, excessRateBp: value }))}
          errors={fieldErrors?.excessRateBp}
        />
      </td>
      <td className="p-2">
        <ReadOnlyMoneyField
          id={`esv-monthly-${year}`}
          label={`${tYears("esvMonthlyKop")} ${year}`}
          labelClassName="sr-only"
          locale={locale}
          valueKop={Number(taxYear.esvMonthlyKop)}
        />
      </td>
      <td className="p-2">
        <NumberField
          id={`income-limit-min-wages-${year}`}
          label={`${tYears("incomeLimitMinWages")} ${year}`}
          labelClassName="sr-only"
          value={form.incomeLimitMinWages}
          onChange={(value) => setForm((current) => ({ ...current, incomeLimitMinWages: value }))}
          errors={fieldErrors?.incomeLimitMinWages}
        />
      </td>
      <td className="p-2">
        <ReadOnlyMoneyField
          id={`income-limit-${year}`}
          label={`${tYears("incomeLimitKop")} ${year}`}
          labelClassName="sr-only"
          locale={locale}
          valueKop={Number(taxYear.incomeLimitKop)}
        />
      </td>
      <td className="p-2">
        <TextField
          id={`limit-warn-thresholds-${year}`}
          label={`${tYears("limitWarnThresholdsPct")} ${year}`}
          labelClassName="sr-only"
          value={form.limitWarnThresholdsPctText}
          onChange={(value) => setForm((current) => ({ ...current, limitWarnThresholdsPctText: value }))}
          errors={fieldErrors?.limitWarnThresholdsPct}
        />
      </td>
      <td className="p-2">
        <NumberField
          id={`esv-deadline-day-${year}`}
          label={`${tYears("esvDeadlineDay")} ${year}`}
          labelClassName="sr-only"
          min={1}
          max={28}
          value={form.esvDeadlineDay}
          onChange={(value) => setForm((current) => ({ ...current, esvDeadlineDay: value }))}
          errors={fieldErrors?.esvDeadlineDay}
        />
      </td>
      <td className="p-2">
        <NumberField
          id={`declaration-days-${year}`}
          label={`${tYears("declarationDays")} ${year}`}
          labelClassName="sr-only"
          value={form.declarationDays}
          onChange={(value) => setForm((current) => ({ ...current, declarationDays: value }))}
          errors={fieldErrors?.declarationDays}
        />
      </td>
      <td className="p-2">
        <NumberField
          id={`tax-payment-days-after-declaration-${year}`}
          label={`${tYears("taxPaymentDaysAfterDeclaration")} ${year}`}
          labelClassName="sr-only"
          value={form.taxPaymentDaysAfterDeclaration}
          onChange={(value) =>
            setForm((current) => ({ ...current, taxPaymentDaysAfterDeclaration: value }))
          }
          errors={fieldErrors?.taxPaymentDaysAfterDeclaration}
        />
      </td>
      <td className="p-2">
        <NumberField
          id={`advance-recommended-day-${year}`}
          label={`${tYears("advanceRecommendedDay")} ${year}`}
          labelClassName="sr-only"
          value={form.advanceRecommendedDay}
          onChange={(value) => setForm((current) => ({ ...current, advanceRecommendedDay: value }))}
          errors={fieldErrors?.advanceRecommendedDay}
        />
      </td>
      <td className="p-2">
        <TextField
          id={`holidays-${year}`}
          label={`${tYears("holidays")} ${year}`}
          labelClassName="sr-only"
          value={form.holidaysText}
          onChange={(value) => setForm((current) => ({ ...current, holidaysText: value }))}
          errors={fieldErrors?.holidays}
        />
      </td>
      <td className="p-2">
        <TextField
          id={`source-${year}`}
          label={`${tYears("source")} ${year}`}
          labelClassName="sr-only"
          value={form.source}
          onChange={(value) => setForm((current) => ({ ...current, source: value }))}
          errors={fieldErrors?.source}
        />
      </td>
      <td className="p-2 text-xs text-muted-foreground">
        {taxYear.verifiedAt
          ? tYears("verified", { date: new Intl.DateTimeFormat(locale).format(new Date(taxYear.verifiedAt)) })
          : tYears("unverified")}
      </td>
      <td className="p-2">
        <div className="flex flex-col gap-2">
          <Button
            type="button"
            size="sm"
            disabled={saveTaxYear.isPending}
            onClick={() => saveTaxYear.mutate({ year, body: toRequest(form) })}
          >
            {saveTaxYear.isPending ? t("saving") : t("save")}
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={verifyTaxYear.isPending || taxYear.verifiedAt !== null}
            onClick={() => verifyTaxYear.mutate(year)}
          >
            {verifyTaxYear.isPending ? tYears("verifying") : tYears("verify")}
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={cloneTaxYear.isPending}
            onClick={() => cloneTaxYear.mutate({ year, next })}
          >
            {cloneTaxYear.isPending ? tYears("cloning") : tYears("cloneToNext", { next })}
          </Button>
          {saveFailure && Object.keys(saveFailure.errors).length === 0 ? (
            <p className="text-xs text-destructive">{`${t("saveFailed")} ${saveFailure.message}`}</p>
          ) : null}
          {actionFailure ? (
            <p className="text-xs text-destructive">{`${t("saveFailed")} ${actionFailure.message}`}</p>
          ) : null}
        </div>
      </td>
    </tr>
  );
}
