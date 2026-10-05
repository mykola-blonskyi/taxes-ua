"use client";

import { useState } from "react";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { problemOf } from "@/data/api/client";
import { useMe } from "@/data/auth/useMe";
import { useApiErrorText } from "@/data/api/useApiErrorText";
import {
  useCloneTaxYear,
  useSaveTaxYear,
  useTaxYears,
  useVerifyTaxYear,
  type TaxYearConfigRequest,
  type TaxYearConfigResponse,
} from "@/data/tax-years/useTaxYears";
import { todayInKyiv } from "@/shared/lib/dates";
import { Button } from "@/shared/ui/button";
import { LoadState } from "@/data/api/LoadState";
import { MoneyField, NumberField, RateField, ReadOnlyMoneyField, TextField } from "@/shared/ui/fields";

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
  group3ApplicationDays: number;
  holidaysText: string;
  militaryLevyAccountEnd: string;
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
    group3ApplicationDays: Number(taxYear.group3ApplicationDays),
    holidaysText: taxYear.holidays.join(", "),
    militaryLevyAccountEnd: taxYear.militaryLevyAccountEnd ?? "",
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
    group3ApplicationDays: form.group3ApplicationDays,
    holidays: form.holidaysText
      .split(",")
      .map((value) => value.trim())
      .filter((value) => value.length > 0),
    militaryLevyAccountEnd: form.militaryLevyAccountEnd === "" ? null : form.militaryLevyAccountEnd,
    source: form.source,
  };
}

export function TaxYearTable() {
  const t = useTranslations("settings");
  const tYears = useTranslations("settings.taxYears");
  const query = useTaxYears();
  const meQuery = useMe();
  const { data } = query;

  if (!data) {
    return <LoadState query={query} loading={t("loading")} failed={t("loadFailed")} />;
  }

  // The api refuses a non-admin's write whatever the screen offers (ADR-005); this only keeps the controls
  // from promising what it will refuse. Until /me answers nothing is offered, so the controls never flash.
  if (!meQuery.data) {
    return <LoadState query={meQuery} loading={t("loading")} failed={t("loadFailed")} />;
  }

  const canEdit = meQuery.data.isAdmin;

  if (data.length === 0) {
    return <p className="text-sm text-muted-foreground">{tYears("empty")}</p>;
  }

  return (
    <div className="flex min-w-0 flex-col gap-4">
      {canEdit ? <NewYearOffer taxYears={data} /> : <p className="text-sm text-muted-foreground">{tYears("readOnly")}</p>}
      <TaxYearRows taxYears={data} canEdit={canEdit} />
    </div>
  );
}

// The next year is due and has no parameters: offer to clone the latest configured year into it, at the top,
// where the owner lands from the dashboard notice and the channel alert. It is due in December (Kyiv), or
// when the current Kyiv year itself has no row (the rollover); earlier than that an unverified copy would
// nag for months and extend the ledger (Rule 9).
function NewYearOffer({ taxYears }: { taxYears: TaxYearConfigResponse[] }) {
  const t = useTranslations("settings");
  const tYears = useTranslations("settings.taxYears");
  const apiText = useApiErrorText();
  const cloneTaxYear = useCloneTaxYear();
  const today = todayInKyiv();
  const current = Number(today.slice(0, 4));
  const years = taxYears.map((taxYear) => Number(taxYear.year));
  const year = Math.max(...years);
  const next = year + 1;
  const due = today.slice(5, 7) === "12" || !years.includes(current);

  if (!due || next > current + 1) {
    return null;
  }

  const failure = cloneTaxYear.error;

  return (
    <section className="flex min-w-0 flex-col gap-2 rounded-lg border border-amber-500/50 bg-amber-500/10 p-4 text-sm">
      <p className="break-words">{tYears("offer.text", { year, next })}</p>
      <Button
        type="button"
        size="sm"
        className="w-fit"
        disabled={cloneTaxYear.isPending}
        onClick={() => cloneTaxYear.mutate({ year, next })}
      >
        {cloneTaxYear.isPending ? tYears("cloning") : tYears("offer.clone", { year, next })}
      </Button>
      {failure ? <p className="text-xs text-destructive">{apiText.withReason(t("saveFailed"), failure)}</p> : null}
    </section>
  );
}

function TaxYearRows({ taxYears: data, canEdit }: { taxYears: TaxYearConfigResponse[]; canEdit: boolean }) {
  const tYears = useTranslations("settings.taxYears");

  return (
    // `relative` makes this the containing block of the fields' sr-only labels. They are absolutely
    // positioned, so without it they escape the scroll box and widen the whole page.
    <div className="relative overflow-x-auto">
      <table className="block w-full border-collapse text-sm md:table">
        <thead className="hidden md:table-header-group">
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
            <th className="min-w-24 p-2 font-medium">{tYears("group3ApplicationDays")}</th>
            <th className="min-w-24 p-2 font-medium">
              {tYears("holidays")}
              <span className="block font-normal">{tYears("holidaysHint")}</span>
            </th>
            <th className="min-w-24 p-2 font-medium">
              {tYears("militaryLevyAccountEnd")}
              <span className="block font-normal">{tYears("militaryLevyAccountEndHint")}</span>
            </th>
            <th className="min-w-24 p-2 font-medium">{tYears("source")}</th>
            <th className="min-w-24 p-2 font-medium">{tYears("verifiedAt")}</th>
            <th className="p-2 font-medium" />
          </tr>
        </thead>
        <tbody className="flex flex-col gap-3 md:table-row-group">
          {data.map((taxYear) => (
            <TaxYearRow key={taxYear.year} taxYear={taxYear} canEdit={canEdit} />
          ))}
        </tbody>
      </table>
    </div>
  );
}

function TaxYearRow({ taxYear, canEdit }: { taxYear: TaxYearConfigResponse; canEdit: boolean }) {
  const t = useTranslations("settings");
  const apiText = useApiErrorText();
  const tYears = useTranslations("settings.taxYears");
  const locale = useLocale();
  const year = Number(taxYear.year);
  const next = year + 1;

  const saveTaxYear = useSaveTaxYear();
  const verifyTaxYear = useVerifyTaxYear();
  const cloneTaxYear = useCloneTaxYear();
  const [form, setForm] = useState<FormState>(() => toFormState(taxYear));

  const saveFailure = saveTaxYear.error;
  const fieldErrors = apiText.fieldTexts(saveFailure);
  const actionFailure = verifyTaxYear.error ?? cloneTaxYear.error;

  return (
    <tr className="grid grid-cols-2 gap-3 rounded-lg border p-3 align-top md:table-row md:rounded-none md:border-0 md:border-b md:p-0">
      <td className="col-span-2 text-base font-semibold md:table-cell md:p-2 md:text-sm md:font-normal">{year}</td>
      <td className="md:table-cell md:p-2">
        <MoneyField
          id={`min-wage-${year}`}
          label={`${tYears("minWageKop")} ${year}`}
          labelClassName="text-xs text-muted-foreground md:sr-only"
          locale={locale}
          valueKop={form.minWageKop}
          onChange={(value) => setForm((current) => ({ ...current, minWageKop: value }))}
          errors={fieldErrors?.minWageKop}
          disabled={!canEdit}
        />
      </td>
      <td className="md:table-cell md:p-2">
        <RateField
          id={`single-tax-rate-${year}`}
          label={`${tYears("singleTaxRateBp")} ${year}`}
          labelClassName="text-xs text-muted-foreground md:sr-only"
          locale={locale}
          valueBp={form.singleTaxRateBp}
          onChange={(value) => setForm((current) => ({ ...current, singleTaxRateBp: value }))}
          errors={fieldErrors?.singleTaxRateBp}
          disabled={!canEdit}
        />
      </td>
      <td className="md:table-cell md:p-2">
        <RateField
          id={`military-levy-rate-${year}`}
          label={`${tYears("militaryLevyRateBp")} ${year}`}
          labelClassName="text-xs text-muted-foreground md:sr-only"
          locale={locale}
          valueBp={form.militaryLevyRateBp}
          onChange={(value) => setForm((current) => ({ ...current, militaryLevyRateBp: value }))}
          errors={fieldErrors?.militaryLevyRateBp}
          disabled={!canEdit}
        />
      </td>
      <td className="md:table-cell md:p-2">
        <RateField
          id={`esv-rate-${year}`}
          label={`${tYears("esvRateBp")} ${year}`}
          labelClassName="text-xs text-muted-foreground md:sr-only"
          locale={locale}
          valueBp={form.esvRateBp}
          onChange={(value) => setForm((current) => ({ ...current, esvRateBp: value }))}
          errors={fieldErrors?.esvRateBp}
          disabled={!canEdit}
        />
      </td>
      <td className="md:table-cell md:p-2">
        <RateField
          id={`excess-rate-${year}`}
          label={`${tYears("excessRateBp")} ${year}`}
          labelClassName="text-xs text-muted-foreground md:sr-only"
          locale={locale}
          valueBp={form.excessRateBp}
          onChange={(value) => setForm((current) => ({ ...current, excessRateBp: value }))}
          errors={fieldErrors?.excessRateBp}
          disabled={!canEdit}
        />
      </td>
      <td className="md:table-cell md:p-2">
        <ReadOnlyMoneyField
          id={`esv-monthly-${year}`}
          label={`${tYears("esvMonthlyKop")} ${year}`}
          labelClassName="text-xs text-muted-foreground md:sr-only"
          locale={locale}
          valueKop={Number(taxYear.esvMonthlyKop)}
        />
      </td>
      <td className="md:table-cell md:p-2">
        <NumberField
          id={`income-limit-min-wages-${year}`}
          label={`${tYears("incomeLimitMinWages")} ${year}`}
          labelClassName="text-xs text-muted-foreground md:sr-only"
          value={form.incomeLimitMinWages}
          onChange={(value) => setForm((current) => ({ ...current, incomeLimitMinWages: value }))}
          errors={fieldErrors?.incomeLimitMinWages}
          disabled={!canEdit}
        />
      </td>
      <td className="md:table-cell md:p-2">
        <ReadOnlyMoneyField
          id={`income-limit-${year}`}
          label={`${tYears("incomeLimitKop")} ${year}`}
          labelClassName="text-xs text-muted-foreground md:sr-only"
          locale={locale}
          valueKop={Number(taxYear.incomeLimitKop)}
        />
      </td>
      <td className="col-span-2 md:table-cell md:p-2">
        <TextField
          hint={tYears("limitWarnThresholdsPctHint")}
          hintClassName="md:hidden"
          id={`limit-warn-thresholds-${year}`}
          label={`${tYears("limitWarnThresholdsPct")} ${year}`}
          labelClassName="text-xs text-muted-foreground md:sr-only"
          value={form.limitWarnThresholdsPctText}
          onChange={(value) => setForm((current) => ({ ...current, limitWarnThresholdsPctText: value }))}
          errors={fieldErrors?.limitWarnThresholdsPct}
          disabled={!canEdit}
        />
      </td>
      <td className="md:table-cell md:p-2">
        <NumberField
          hint={tYears("esvDeadlineDayHint")}
          hintClassName="md:hidden"
          id={`esv-deadline-day-${year}`}
          label={`${tYears("esvDeadlineDay")} ${year}`}
          labelClassName="text-xs text-muted-foreground md:sr-only"
          min={1}
          max={28}
          value={form.esvDeadlineDay}
          onChange={(value) => setForm((current) => ({ ...current, esvDeadlineDay: value }))}
          errors={fieldErrors?.esvDeadlineDay}
          disabled={!canEdit}
        />
      </td>
      <td className="md:table-cell md:p-2">
        <NumberField
          id={`declaration-days-${year}`}
          label={`${tYears("declarationDays")} ${year}`}
          labelClassName="text-xs text-muted-foreground md:sr-only"
          value={form.declarationDays}
          onChange={(value) => setForm((current) => ({ ...current, declarationDays: value }))}
          errors={fieldErrors?.declarationDays}
          disabled={!canEdit}
        />
      </td>
      <td className="md:table-cell md:p-2">
        <NumberField
          id={`tax-payment-days-after-declaration-${year}`}
          label={`${tYears("taxPaymentDaysAfterDeclaration")} ${year}`}
          labelClassName="text-xs text-muted-foreground md:sr-only"
          value={form.taxPaymentDaysAfterDeclaration}
          onChange={(value) =>
            setForm((current) => ({ ...current, taxPaymentDaysAfterDeclaration: value }))
          }
          errors={fieldErrors?.taxPaymentDaysAfterDeclaration}
          disabled={!canEdit}
        />
      </td>
      <td className="md:table-cell md:p-2">
        <NumberField
          id={`advance-recommended-day-${year}`}
          label={`${tYears("advanceRecommendedDay")} ${year}`}
          labelClassName="text-xs text-muted-foreground md:sr-only"
          value={form.advanceRecommendedDay}
          onChange={(value) => setForm((current) => ({ ...current, advanceRecommendedDay: value }))}
          errors={fieldErrors?.advanceRecommendedDay}
          disabled={!canEdit}
        />
      </td>
      <td className="md:table-cell md:p-2">
        <NumberField
          id={`group3-application-days-${year}`}
          label={`${tYears("group3ApplicationDays")} ${year}`}
          labelClassName="text-xs text-muted-foreground md:sr-only"
          value={form.group3ApplicationDays}
          onChange={(value) => setForm((current) => ({ ...current, group3ApplicationDays: value }))}
          errors={fieldErrors?.group3ApplicationDays}
          disabled={!canEdit}
        />
      </td>
      <td className="col-span-2 md:table-cell md:p-2">
        <TextField
          hint={tYears("holidaysHint")}
          hintClassName="md:hidden"
          id={`holidays-${year}`}
          label={`${tYears("holidays")} ${year}`}
          labelClassName="text-xs text-muted-foreground md:sr-only"
          value={form.holidaysText}
          onChange={(value) => setForm((current) => ({ ...current, holidaysText: value }))}
          errors={fieldErrors?.holidays}
          disabled={!canEdit}
        />
      </td>
      <td className="col-span-2 md:table-cell md:p-2">
        <TextField
          type="date"
          hint={tYears("militaryLevyAccountEndHint")}
          hintClassName="md:hidden"
          id={`military-levy-account-end-${year}`}
          label={`${tYears("militaryLevyAccountEnd")} ${year}`}
          labelClassName="text-xs text-muted-foreground md:sr-only"
          value={form.militaryLevyAccountEnd}
          onChange={(value) => setForm((current) => ({ ...current, militaryLevyAccountEnd: value }))}
          errors={fieldErrors?.militaryLevyAccountEnd}
        />
      </td>
      <td className="col-span-2 md:table-cell md:p-2">
        <TextField
          id={`source-${year}`}
          label={`${tYears("source")} ${year}`}
          labelClassName="text-xs text-muted-foreground md:sr-only"
          value={form.source}
          onChange={(value) => setForm((current) => ({ ...current, source: value }))}
          errors={fieldErrors?.source}
          disabled={!canEdit}
        />
      </td>
      <td className="col-span-2 text-xs text-muted-foreground md:table-cell md:p-2">
        <span className="font-medium md:hidden">{tYears("verifiedAt")}: </span>
        {taxYear.verifiedAt
          ? tYears("verified", { date: new Intl.DateTimeFormat(locale).format(new Date(taxYear.verifiedAt)) })
          : tYears("unverified")}
      </td>
      <td className="col-span-2 md:table-cell md:p-2">
        <div className="grid grid-cols-2 gap-2 md:flex md:flex-col">
          {canEdit ? (
            <>
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
            </>
          ) : null}
          <Button asChild variant="outline" size="sm">
            <Link href={`/history?entity=TaxYearConfig&id=${year}`}>{tYears("history")}</Link>
          </Button>
          {saveFailure && Object.keys(problemOf(saveFailure)?.fieldCodes ?? {}).length === 0 ? (
            <p className="text-xs text-destructive">{apiText.withReason(t("saveFailed"), saveFailure)}</p>
          ) : null}
          {actionFailure ? (
            <p className="text-xs text-destructive">{apiText.withReason(t("saveFailed"), actionFailure)}</p>
          ) : null}
        </div>
      </td>
    </tr>
  );
}
