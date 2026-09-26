"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import { useSaveSettings, useSettings, type SettingsRequest } from "@/data/settings/useSettings";
import { locales } from "@/i18n/locales";
import { Button } from "@/shared/ui/button";
import { CheckboxField, SelectField, TextField } from "./fields";

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
  };
}

function toRequest(form: FormState): SettingsRequest {
  return {
    ...form,
    fopRegistrationDate: form.fopRegistrationDate === "" ? null : form.fopRegistrationDate,
  };
}

export function FopSettingsForm() {
  const t = useTranslations("settings");
  const { data, isLoading, isError } = useSettings();

  if (isLoading) {
    return <p className="text-sm text-muted-foreground">{t("loading")}</p>;
  }

  if (isError || !data) {
    return <p className="text-sm text-destructive">{t("loadFailed")}</p>;
  }

  return <SettingsFormBody initial={data} />;
}

function SettingsFormBody({ initial }: { initial: SettingsRequest }) {
  const t = useTranslations("settings");
  const tFop = useTranslations("settings.fop");
  const tLanguage = useTranslations("language");
  const tTheme = useTranslations("theme");
  const saveSettings = useSaveSettings();
  const [form, setForm] = useState<FormState>(() => toFormState(initial));

  const failure = saveSettings.error instanceof ApiError ? saveSettings.error : null;
  const fieldErrors = failure?.errors;
  const rejectedFields = Object.keys(fieldErrors ?? {}).length > 0;

  function toggleWeekendDay(day: DayOfWeek, enabled: boolean) {
    setForm((current) => ({
      ...current,
      weekendDays: enabled
        ? [...current.weekendDays, day]
        : current.weekendDays.filter((existing) => existing !== day),
    }));
  }

  return (
    <form
      className="flex flex-col gap-4"
      onSubmit={(event) => {
        event.preventDefault();
        saveSettings.mutate(toRequest(form));
      }}
    >
      {rejectedFields ? (
        <p className="rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">
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

      <fieldset className="flex flex-col gap-2">
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
        {fieldErrors?.weekendDays && fieldErrors.weekendDays.length > 0 ? (
          <ul className="text-xs text-destructive">
            {fieldErrors.weekendDays.map((message) => (
              <li key={message}>{message}</li>
            ))}
          </ul>
        ) : null}
      </fieldset>

      {failure && !rejectedFields ? (
        <p className="text-sm text-destructive">{`${t("saveFailed")} ${failure.message}`}</p>
      ) : null}

      <div>
        <Button type="submit" disabled={saveSettings.isPending}>
          {saveSettings.isPending ? t("saving") : t("save")}
        </Button>
      </div>
    </form>
  );
}
