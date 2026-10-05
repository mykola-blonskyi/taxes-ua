"use client";

import { useState, type ReactNode } from "react";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import { useApiErrorText } from "@/data/api/useApiErrorText";
import { useDpsStatus, useSaveDpsStatus, type DpsStatusRequest, type DpsStatusResponse } from "@/data/settings/useDpsStatus";
import { formatDateOnly, todayInKyiv } from "@/shared/lib/dates";
import { Button } from "@/shared/ui/button";
import { LoadState } from "@/data/api/LoadState";
import { CheckboxField, FieldErrors, SelectField, TextField, FieldForm } from "@/shared/ui/fields";

type Start = "registration" | "quarter";

type FormState = {
  fopRegistered: boolean;
  confirmed: boolean;
  confirmedOn: string;
  receiptNumber: string;
  esvRegistered: boolean;
  accountsRegistered: boolean;
  start: Start;
  quarterStart: string;
};

type Quarter = { year: number; quarter: number };

function quarterOf(date: string): Quarter {
  return { year: Number(date.slice(0, 4)), quarter: Math.ceil(Number(date.slice(5, 7)) / 3) };
}

function next({ year, quarter }: Quarter): Quarter {
  return quarter === 4 ? { year: year + 1, quarter: 1 } : { year, quarter: quarter + 1 };
}

function firstDayOf({ year, quarter }: Quarter): string {
  return `${year}-${String(3 * quarter - 2).padStart(2, "0")}-01`;
}

// Tax Code 298.1.4 allows only the first day of a quarter after the registration date. Two years of them
// cover the realistic choice; a later stored date stays on the list so the form never hides what is saved.
function quarterStartsAfter(registered: string, today: string, stored: string | null): string[] {
  const starts: string[] = [];
  let quarter = next(quarterOf(registered));
  const horizon = firstDayOf(next(next(next(next(quarterOf(today))))));

  while (starts.length < 8 || firstDayOf(quarter) <= horizon) {
    starts.push(firstDayOf(quarter));
    quarter = next(quarter);
  }

  return stored && stored > registered && !starts.includes(stored) ? [...starts, stored] : starts;
}

function daysFrom(from: string, to: string): number {
  return (Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000;
}

function toFormState(status: DpsStatusResponse): FormState {
  const fromQuarter = status.group3Since !== null && status.group3Since !== status.fopRegistrationDate;

  return {
    fopRegistered: status.fopRegistered,
    confirmed: status.confirmation !== null,
    confirmedOn: status.confirmation?.confirmedOn ?? "",
    receiptNumber: status.confirmation?.receiptNumber ?? "",
    esvRegistered: status.esvRegistered,
    accountsRegistered: status.accountsRegistered,
    start: fromQuarter ? "quarter" : "registration",
    quarterStart: fromQuarter ? status.group3Since! : "",
  };
}

function toRequest(form: FormState): DpsStatusRequest {
  return {
    group3Since: form.start === "quarter" ? form.quarterStart : null,
    confirmation: form.confirmed ? { confirmedOn: form.confirmedOn, receiptNumber: form.receiptNumber } : null,
    fopRegistered: form.fopRegistered,
    esvRegistered: form.esvRegistered,
    accountsRegistered: form.accountsRegistered,
  };
}

export function DpsStatusSection() {
  const t = useTranslations("settings");
  const query = useDpsStatus();
  const { data } = query;

  if (!data) {
    return <LoadState query={query} loading={t("loading")} failed={t("loadFailed")} />;
  }

  return <DpsStatusForm status={data} />;
}

function DpsStatusForm({ status }: { status: DpsStatusResponse }) {
  const t = useTranslations("settings");
  const tDps = useTranslations("settings.dps");
  const apiText = useApiErrorText();
  const locale = useLocale();
  const save = useSaveDpsStatus();
  const [form, setForm] = useState<FormState>(() => toFormState(status));
  const today = todayInKyiv();
  const registered = status.fopRegistrationDate;
  const deadline = status.applicationDeadline;

  const failure = save.error instanceof ApiError ? save.error : null;
  const rejected = Object.keys(failure?.fieldCodes ?? {}).length > 0;

  function fieldErrors(key: string): string[] | undefined {
    return failure?.fieldCodes[key]?.map(apiText.ofCode);
  }

  const quarterStarts = registered ? quarterStartsAfter(registered, today, status.group3Since) : [];
  const showDeadline = deadline !== null && !form.confirmed && form.start === "registration";
  const update = (patch: Partial<FormState>) => setForm((current) => ({ ...current, ...patch }));

  return (
    <FieldForm quietErrors={Boolean(rejected)}
      className="flex min-w-0 flex-col gap-6"
      onSubmit={(event) => {
        event.preventDefault();
        save.mutate(toRequest(form));
      }}
    >
      <div className="flex min-w-0 flex-col gap-1">
        <h3 className="text-base font-semibold">{tDps("title")}</h3>
        <p className="text-sm text-muted-foreground">{tDps("intro")}</p>
        <p className="text-xs text-muted-foreground">{tDps("whereToCheck")}</p>
      </div>

      {rejected ? (
        <p role="alert" className="rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">
          {t("validationError")}
        </p>
      ) : null}

      <ul className="flex min-w-0 flex-col divide-y rounded-lg border">
        <Item>
          <CheckboxField
            id="dps-fop-registered"
            label={tDps("fopRegistered")}
            labelClassName="text-sm font-medium"
            checked={form.fopRegistered}
            onChange={(fopRegistered) => update({ fopRegistered })}
          />
          {registered ? (
            <p className="text-xs text-muted-foreground">
              {tDps("registrationDate", { date: formatDateOnly(registered, locale) })}
            </p>
          ) : (
            <p className="text-xs text-muted-foreground">
              {tDps("registrationDateNotSet")}{" "}
              <Link href="/settings?tab=fop" className="font-medium text-primary underline-offset-4 hover:underline">
                {tDps("setRegistrationDate")}
              </Link>
            </p>
          )}
        </Item>

        <Item>
          <CheckboxField
            id="dps-group3-confirmed"
            label={tDps("group3Accepted")}
            labelClassName="text-sm font-medium"
            checked={form.confirmed}
            onChange={(confirmed) => update({ confirmed })}
          />
          <p className="text-xs text-muted-foreground">{tDps("group3AcceptedHint")}</p>
          {form.confirmed ? (
            <div className="grid min-w-0 gap-3 sm:grid-cols-2">
              <TextField
                id="dps-confirmed-on"
                type="date"
                label={tDps("confirmedOn")}
                value={form.confirmedOn}
                min={registered ?? undefined}
                max={today}
                required
                onChange={(confirmedOn) => update({ confirmedOn })}
                errors={fieldErrors("confirmation.confirmedOn")}
              />
              <TextField
                id="dps-receipt-number"
                label={tDps("receiptNumber")}
                value={form.receiptNumber}
                maxLength={64}
                required
                onChange={(receiptNumber) => update({ receiptNumber })}
                errors={fieldErrors("confirmation.receiptNumber")}
              />
            </div>
          ) : null}
        </Item>

        <Item>
          <CheckboxField
            id="dps-esv-registered"
            label={tDps("esvRegistered")}
            labelClassName="text-sm font-medium"
            checked={form.esvRegistered}
            onChange={(esvRegistered) => update({ esvRegistered })}
          />
          <p className="text-xs text-muted-foreground">{tDps("esvRegisteredHint")}</p>
        </Item>

        <Item>
          <CheckboxField
            id="dps-accounts-registered"
            label={tDps("accountsRegistered")}
            labelClassName="text-sm font-medium"
            checked={form.accountsRegistered}
            onChange={(accountsRegistered) => update({ accountsRegistered })}
          />
          <p className="text-xs text-muted-foreground">{tDps("accountsRegisteredHint")}</p>
        </Item>
      </ul>

      <fieldset
        className="flex min-w-0 flex-col gap-2"
        aria-describedby={fieldErrors("group3Since")?.length ? "group3-since-error" : undefined}
      >
        <legend className="text-base font-semibold">{tDps("since")}</legend>
        <p className="text-xs text-muted-foreground">{tDps("sinceHint")}</p>
        <Radio
          id="dps-since-registration"
          checked={form.start === "registration"}
          onChange={() => update({ start: "registration" })}
          label={
            registered && deadline
              ? tDps("sinceRegistrationWithin", { days: daysFrom(registered, deadline) })
              : tDps("sinceRegistration")
          }
        />
        <Radio
          id="dps-since-quarter"
          checked={form.start === "quarter"}
          disabled={!registered}
          onChange={() => update({ start: "quarter", quarterStart: form.quarterStart || quarterStarts[0] || "" })}
          label={tDps("sinceQuarter")}
        />
        {form.start === "quarter" && registered ? (
          <div className="min-w-0 pl-6 sm:max-w-xs">
            <SelectField
              id="dps-quarter-start"
              label={tDps("quarterStart")}
              value={form.quarterStart}
              onChange={(quarterStart) => update({ quarterStart })}
              options={quarterStarts.map((start) => ({
                value: start,
                label: tDps("quarterOption", { ...quarterOf(start), date: formatDateOnly(start, locale) }),
              }))}
            />
          </div>
        ) : null}
        <FieldErrors id="group3-since-error" errors={fieldErrors("group3Since")} />
        {showDeadline ? (
          <p className="text-sm text-amber-700 dark:text-amber-400">
            {deadline >= today
              ? tDps("applyBy", { date: formatDateOnly(deadline, locale) })
              : tDps("applicationWindowPassed", { date: formatDateOnly(deadline, locale) })}
          </p>
        ) : null}
        {form.start === "quarter" ? <p className="text-xs text-muted-foreground">{tDps("sinceQuarterHint")}</p> : null}
      </fieldset>

      {failure && !rejected ? <p className="text-sm text-destructive">{apiText.withReason(t("saveFailed"), failure)}</p> : null}

      <div className="flex flex-wrap items-center gap-3">
        <Button type="submit" disabled={save.isPending}>
          {save.isPending ? t("saving") : t("save")}
        </Button>
        {save.isSuccess ? (
          <p role="status" className="text-sm text-emerald-700 dark:text-emerald-400">
            {tDps("saved")}
          </p>
        ) : null}
      </div>
    </FieldForm>
  );
}

function Item({ children }: { children: ReactNode }) {
  return <li className="flex min-w-0 flex-col gap-1.5 p-3">{children}</li>;
}

function Radio({
  id,
  label,
  checked,
  disabled,
  onChange,
}: {
  id: string;
  label: string;
  checked: boolean;
  disabled?: boolean;
  onChange: () => void;
}) {
  return (
    <div className="flex min-w-0 items-start gap-2 pointer-coarse:items-center">
      <input
        id={id}
        type="radio"
        name="dps-group3-since"
        checked={checked}
        disabled={disabled}
        onChange={onChange}
        className="mt-0.5 size-4 shrink-0 disabled:opacity-50 pointer-coarse:mt-0 pointer-coarse:size-6"
      />
      <label htmlFor={id} className="min-w-0 flex-1 break-words text-sm pointer-coarse:flex pointer-coarse:min-h-11 pointer-coarse:items-center">
        {label}
      </label>
    </div>
  );
}
