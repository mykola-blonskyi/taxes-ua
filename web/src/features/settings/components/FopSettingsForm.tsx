"use client";

import { useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useDataReplacements } from "@/data/backup/backup";
import { useApiErrorText } from "@/data/api/useApiErrorText";
import { useSaveSettings, useSettings, type SettingsRequest } from "@/data/settings/useSettings";
import { locales } from "@/i18n/locales";
import { Button } from "@/shared/ui/button";
import { LoadState } from "@/data/api/LoadState";
import { CheckboxField, FieldErrors, SelectField, TextField, FieldForm } from "@/shared/ui/fields";

type PaymentMode = SettingsRequest["paymentMode"];
type EsvRegistrationMonthPolicy = SettingsRequest["esvRegistrationMonthPolicy"];
type DayOfWeek = SettingsRequest["weekendDays"][number];

const paymentModeOptions = ["Quarterly", "MonthlyAdvance"] as const satisfies readonly PaymentMode[];
const esvRegistrationMonthPolicyOptions = [
  "FullMonth",
  "Prorated",
] as const satisfies readonly EsvRegistrationMonthPolicy[];
const weekdayOptions = [
  "Sunday",
  "Monday",
  "Tuesday",
  "Wednesday",
  "Thursday",
  "Friday",
  "Saturday",
] as const satisfies readonly DayOfWeek[];
const weekdayLabelKeys = {
  Sunday: "weekdays.sunday",
  Monday: "weekdays.monday",
  Tuesday: "weekdays.tuesday",
  Wednesday: "weekdays.wednesday",
  Thursday: "weekdays.thursday",
  Friday: "weekdays.friday",
  Saturday: "weekdays.saturday",
} as const satisfies Record<DayOfWeek, string>;
const themeOptions = ["light", "dark", "system"] as const;
const currencyOptions = ["UAH", "USD", "EUR"] as const;

type FormState = {
  fopRegistrationDate: string;
  paymentMode: PaymentMode;
  esvRegistrationMonthPolicy: EsvRegistrationMonthPolicy;
  esvExempt: boolean;
  taxPaymentCountsFromStatutoryDeclarationDate: boolean;
  shiftTaxPaymentFromWeekend: boolean;
  weekendDays: DayOfWeek[];
  locale: string;
  theme: string;
  defaultCurrency: string;
  backOnGroup3FromYear: string;
  backOnGroup3FromQuarter: string;
};

function toFormState(settings: SettingsRequest): FormState {
  return {
    fopRegistrationDate: settings.fopRegistrationDate ?? "",
    paymentMode: settings.paymentMode,
    esvRegistrationMonthPolicy: settings.esvRegistrationMonthPolicy,
    esvExempt: settings.esvExempt,
    taxPaymentCountsFromStatutoryDeclarationDate: settings.taxPaymentCountsFromStatutoryDeclarationDate,
    shiftTaxPaymentFromWeekend: settings.shiftTaxPaymentFromWeekend,
    weekendDays: settings.weekendDays,
    locale: settings.locale,
    theme: settings.theme,
    defaultCurrency: settings.defaultCurrency,
    backOnGroup3FromYear: settings.backOnGroup3From ? String(settings.backOnGroup3From.year) : "",
    backOnGroup3FromQuarter: settings.backOnGroup3From ? String(settings.backOnGroup3From.quarter) : "",
  };
}

// Half a quarter is sent as zeros rather than dropped, so the api rejects it under backOnGroup3From.
function toRequest({ backOnGroup3FromYear, backOnGroup3FromQuarter, ...form }: FormState): SettingsRequest {
  return {
    ...form,
    fopRegistrationDate: form.fopRegistrationDate === "" ? null : form.fopRegistrationDate,
    backOnGroup3From:
      backOnGroup3FromYear === "" && backOnGroup3FromQuarter === ""
        ? null
        : { year: Number(backOnGroup3FromYear) || 0, quarter: Number(backOnGroup3FromQuarter) || 0 },
  };
}

export function FopSettingsForm() {
  const t = useTranslations("settings");
  const query = useSettings();
  const { data } = query;
  const replacements = useDataReplacements();

  if (!data) {
    return <LoadState query={query} loading={t("loading")} failed={t("loadFailed")} />;
  }

  return <SettingsFormBody key={replacements} initial={data} />;
}

function SettingsFormBody({ initial }: { initial: SettingsRequest }) {
  const t = useTranslations("settings");
  const tFop = useTranslations("settings.fop");
  const apiText = useApiErrorText();
  const tLanguage = useTranslations("language");
  const tTheme = useTranslations("theme");
  const saveSettings = useSaveSettings();
  const [form, setForm] = useState<FormState>(() => toFormState(initial));

  const failure = saveSettings.error;
  const fieldErrors = apiText.fieldTexts(failure);
  const rejectedFields = Object.keys(fieldErrors).length > 0;

  function toggleWeekendDay(day: DayOfWeek, enabled: boolean) {
    setForm((current) => ({
      ...current,
      weekendDays: enabled
        ? [...current.weekendDays, day]
        : current.weekendDays.filter((existing) => existing !== day),
    }));
  }

  return (
    <FieldForm quietErrors={Boolean(rejectedFields)}
      className="flex flex-col gap-4"
      onSubmit={(event) => {
        event.preventDefault();
        saveSettings.mutate(toRequest(form));
      }}
    >
      {rejectedFields ? (
        <p role="alert" className="rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">
          {t("validationError")}
        </p>
      ) : null}

      <div className="grid gap-4 sm:grid-cols-2">
        <TextField
          id="fop-registration-date"
          label={tFop("fopRegistrationDate")}
          type="date"
          value={form.fopRegistrationDate}
          onChange={(value) => setForm((current) => ({ ...current, fopRegistrationDate: value }))}
          errors={fieldErrors?.fopRegistrationDate}
        />

        <SelectField
          id="payment-mode"
          label={tFop("paymentMode")}
          value={form.paymentMode}
          onChange={(value) => setForm((current) => ({ ...current, paymentMode: value as PaymentMode }))}
          options={paymentModeOptions.map((mode) => ({
            value: mode,
            label: mode === "Quarterly" ? tFop("paymentModeQuarterly") : tFop("paymentModeMonthlyAdvance"),
          }))}
          errors={fieldErrors?.paymentMode}
        />

        <SelectField
          id="esv-registration-month-policy"
          label={tFop("esvRegistrationMonthPolicy")}
          value={form.esvRegistrationMonthPolicy}
          onChange={(value) =>
            setForm((current) => ({
              ...current,
              esvRegistrationMonthPolicy: value as EsvRegistrationMonthPolicy,
            }))
          }
          options={esvRegistrationMonthPolicyOptions.map((policy) => ({
            value: policy,
            label:
              policy === "FullMonth"
                ? tFop("esvRegistrationMonthPolicyFullMonth")
                : tFop("esvRegistrationMonthPolicyProrated"),
          }))}
          errors={fieldErrors?.esvRegistrationMonthPolicy}
        />

        <SelectField
          id="locale"
          label={tFop("locale")}
          value={form.locale}
          onChange={(value) => setForm((current) => ({ ...current, locale: value }))}
          options={locales.map((code) => ({ value: code, label: tLanguage(code) }))}
          errors={fieldErrors?.locale}
        />

        <SelectField
          id="theme"
          label={tFop("theme")}
          value={form.theme}
          onChange={(value) => setForm((current) => ({ ...current, theme: value }))}
          options={themeOptions.map((value) => ({ value, label: tTheme(value) }))}
          errors={fieldErrors?.theme}
        />

        <SelectField
          id="default-currency"
          label={tFop("defaultCurrency")}
          value={form.defaultCurrency}
          onChange={(value) => setForm((current) => ({ ...current, defaultCurrency: value }))}
          options={currencyOptions.map((code) => ({ value: code, label: code }))}
          errors={fieldErrors?.defaultCurrency}
        />
      </div>

      <div className="flex flex-col gap-2">
        <CheckboxField
          id="esv-exempt"
          label={tFop("esvExempt")}
          checked={form.esvExempt}
          onChange={(checked) => setForm((current) => ({ ...current, esvExempt: checked }))}
        />
        <CheckboxField
          id="tax-payment-counts-from-statutory-declaration-date"
          label={tFop("taxPaymentCountsFromStatutoryDeclarationDate")}
          checked={form.taxPaymentCountsFromStatutoryDeclarationDate}
          onChange={(checked) =>
            setForm((current) => ({ ...current, taxPaymentCountsFromStatutoryDeclarationDate: checked }))
          }
        />
        <CheckboxField
          id="shift-tax-payment-from-weekend"
          label={tFop("shiftTaxPaymentFromWeekend")}
          checked={form.shiftTaxPaymentFromWeekend}
          onChange={(checked) => setForm((current) => ({ ...current, shiftTaxPaymentFromWeekend: checked }))}
        />
      </div>

      <fieldset
        className="flex flex-col gap-2"
        aria-describedby={fieldErrors?.weekendDays?.length ? "weekend-days-error" : undefined}
      >
        <legend className="text-sm font-medium">{tFop("weekendDays")}</legend>
        <div className="flex flex-wrap gap-4">
          {weekdayOptions.map((day) => (
            <CheckboxField
              key={day}
              id={`weekend-day-${day}`}
              label={tFop(weekdayLabelKeys[day])}
              checked={form.weekendDays.includes(day)}
              onChange={(checked) => toggleWeekendDay(day, checked)}
            />
          ))}
        </div>
        <FieldErrors id="weekend-days-error" errors={fieldErrors?.weekendDays} />
      </fieldset>

      <fieldset
        className="flex flex-col gap-2"
        aria-describedby={fieldErrors?.backOnGroup3From?.length ? "back-on-group-3-error" : undefined}
      >
        <legend className="text-sm font-medium">{tFop("backOnGroup3")}</legend>
        <p className="text-xs text-muted-foreground">{tFop("backOnGroup3Hint")}</p>
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField
            id="back-on-group-3-year"
            label={tFop("backOnGroup3Year")}
            type="number"
            inputMode="numeric"
            value={form.backOnGroup3FromYear}
            onChange={(value) => setForm((current) => ({ ...current, backOnGroup3FromYear: value }))}
          />
          <SelectField
            id="back-on-group-3-quarter"
            label={tFop("backOnGroup3Quarter")}
            value={form.backOnGroup3FromQuarter}
            onChange={(value) => setForm((current) => ({ ...current, backOnGroup3FromQuarter: value }))}
            options={[
              { value: "", label: tFop("backOnGroup3None") },
              ...[1, 2, 3, 4].map((quarter) => ({ value: String(quarter), label: `Q${quarter}` })),
            ]}
          />
        </div>
        <FieldErrors id="back-on-group-3-error" errors={fieldErrors?.backOnGroup3From} />
      </fieldset>

      {failure && !rejectedFields ? (
        <p className="text-sm text-destructive">{apiText.withReason(t("saveFailed"), failure)}</p>
      ) : null}

      <div className="flex flex-wrap items-center gap-2">
        <Button type="submit" disabled={saveSettings.isPending}>
          {saveSettings.isPending ? t("saving") : t("save")}
        </Button>
        <Button asChild variant="outline">
          <Link href="/history?entity=Settings">{tFop("history")}</Link>
        </Button>
      </div>
    </FieldForm>
  );
}
