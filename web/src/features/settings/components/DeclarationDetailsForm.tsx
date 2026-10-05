"use client";

import { useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import { useApiErrorText } from "@/data/api/useApiErrorText";
import {
  useDeclarationDetails,
  useKvedClasses,
  useSaveDeclarationDetails,
  type DeclarationDetailsResponse,
} from "@/data/declarations/useDeclarations";
import { Button } from "@/shared/ui/button";
import { LoadState } from "@/data/api/LoadState";
import { FieldErrors, TextAreaField, TextField, FieldForm } from "@/shared/ui/fields";

const maxKvedCodes = 20;

const kvedShape = /^\d{2}\.\d{2}$/;

type FormState = {
  region: string;
  district: string;
  officeName: string;
  kvedCodes: string[];
  address: string;
  fullName: string;
  phone: string;
  reportEmail: string;
};

function twoDigits(value: number | string | null): string {
  return value === null ? "" : String(value).padStart(2, "0");
}

function toFormState(details: DeclarationDetailsResponse): FormState {
  return {
    region: twoDigits(details.taxOfficeRegion),
    district: twoDigits(details.taxOfficeDistrict),
    officeName: details.taxOfficeName,
    kvedCodes: details.kvedCodes.length > 0 ? details.kvedCodes : [""],
    address: details.address,
    fullName: details.fullName,
    phone: details.phone,
    reportEmail: details.reportEmail,
  };
}

export function DeclarationDetailsForm() {
  const t = useTranslations("settings");
  const query = useDeclarationDetails();
  const { data } = query;

  if (!data) {
    return <LoadState query={query} loading={t("loading")} failed={t("loadFailed")} />;
  }

  return <DeclarationDetailsFormBody details={data} />;
}

function DeclarationDetailsFormBody({ details }: { details: DeclarationDetailsResponse }) {
  const t = useTranslations("settings");
  const tDeclaration = useTranslations("settings.declaration");
  const tFields = useTranslations("declaration.fields");
  const apiText = useApiErrorText();
  const save = useSaveDeclarationDetails();
  const kvedClasses = useKvedClasses();
  const [form, setForm] = useState<FormState>(() => toFormState(details));

  const failure = save.error instanceof ApiError ? save.error : null;
  const rejected = Object.keys(failure?.fieldCodes ?? {}).length > 0;
  const missing = details.missingDetails;
  const unknownKved = details.unknownKvedCodes;
  const suggestedEmail = form.reportEmail === "" ? details.confirmedEmail : null;
  const sentKved = form.kvedCodes.flatMap((code, index) => (code.trim() === "" ? [] : [index]));

  function fieldErrors(key: string): string[] | undefined {
    return failure?.fieldCodes[key]?.map(apiText.ofCode);
  }

  function kvedErrors(index: number): string[] | undefined {
    const sent = sentKved.indexOf(index);

    return sent < 0 ? undefined : fieldErrors(`kvedCodes[${sent}]`);
  }

  // Typing a code is touching it (ADR-030), so an unknown one is worded before any save.
  function kvedAnnotation(code: string): { hint?: string; errors?: string[] } {
    const typed = code.trim();
    const names = kvedClasses.data;

    if (!names || !kvedShape.test(typed)) {
      return {};
    }

    const name = names.get(typed);

    return name === undefined ? { errors: [apiText.ofCode("kved_unknown")] } : { hint: name };
  }

  const setDigits = (field: "region" | "district") => (value: string) =>
    setForm((current) => ({ ...current, [field]: value.replace(/\D/g, "") }));

  const setKved = (index: number) => (value: string) =>
    setForm((current) => ({
      ...current,
      kvedCodes: current.kvedCodes.map((code, at) => (at === index ? value : code)),
    }));

  function removeKved(index: number) {
    setForm((current) => {
      const kvedCodes = current.kvedCodes.filter((_, at) => at !== index);

      return { ...current, kvedCodes: kvedCodes.length > 0 ? kvedCodes : [""] };
    });
  }

  return (
    <FieldForm quietErrors={Boolean(rejected)}
      className="flex min-w-0 flex-col gap-6"
      onSubmit={(event) => {
        event.preventDefault();
        save.mutate({
          taxOfficeRegion: form.region === "" ? null : Number(form.region),
          taxOfficeDistrict: form.district === "" ? null : Number(form.district),
          taxOfficeName: form.officeName,
          kvedCodes: sentKved.map((index) => form.kvedCodes[index].trim()),
          address: form.address,
          fullName: form.fullName,
          phone: form.phone,
          reportEmail: form.reportEmail,
        });
      }}
    >
      {missing.length > 0 ? (
        <p className="rounded-lg border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-800 dark:text-amber-300">
          {tDeclaration("missing", { fields: missing.map((field) => tFields(field)).join(", ") })}
        </p>
      ) : unknownKved.length > 0 ? (
        <p className="rounded-lg border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-800 dark:text-amber-300">
          {tDeclaration("kvedUnknownBanner", { codes: unknownKved.join(", ") })}
        </p>
      ) : (
        <p className="text-sm text-emerald-700 dark:text-emerald-400">{tDeclaration("complete")}</p>
      )}

      {rejected ? (
        <p role="alert" className="rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">
          {t("validationError")}
        </p>
      ) : null}

      <section className="flex min-w-0 flex-col gap-2">
        <dl className="grid min-w-0 gap-x-4 gap-y-2 text-sm sm:grid-cols-2">
          <div className="flex min-w-0 flex-col gap-0.5">
            <dt className="text-muted-foreground">{tDeclaration("name")}</dt>
            <dd className="break-words">{details.name || tDeclaration("notSet")}</dd>
          </div>
          <div className="flex min-w-0 flex-col gap-0.5">
            <dt className="text-muted-foreground">{tDeclaration("rnokpp")}</dt>
            <dd className="tabular-nums">{details.rnokpp || tDeclaration("notSet")}</dd>
          </div>
        </dl>
        <p className="text-xs text-muted-foreground">{tDeclaration("fromInvoicing")}</p>
        <TextField
          id="declaration-full-name"
          label={tDeclaration("fullName")}
          hint={tDeclaration("fullNameHint")}
          autoComplete="name"
          value={form.fullName}
          onChange={(fullName) => setForm((current) => ({ ...current, fullName }))}
          errors={fieldErrors("fullName")}
        />
      </section>

      <fieldset className="flex min-w-0 flex-col gap-2">
        <legend className="text-base font-semibold">{tDeclaration("taxOffice")}</legend>
        <div className="grid max-w-xs grid-cols-2 gap-3">
          <TextField
            id="tax-office-region"
            label={tDeclaration("region")}
            inputMode="numeric"
            maxLength={2}
            value={form.region}
            onChange={setDigits("region")}
            errors={fieldErrors("taxOfficeRegion")}
          />
          <TextField
            id="tax-office-district"
            label={tDeclaration("district")}
            inputMode="numeric"
            maxLength={2}
            value={form.district}
            onChange={setDigits("district")}
            errors={fieldErrors("taxOfficeDistrict")}
          />
        </div>
        <p className="text-xs text-muted-foreground">{tDeclaration("taxOfficeHint")}</p>
        <TextField
          id="tax-office-name"
          label={tDeclaration("officeName")}
          hint={tDeclaration("officeNameHint")}
          value={form.officeName}
          onChange={(officeName) => setForm((current) => ({ ...current, officeName }))}
          errors={fieldErrors("taxOfficeName")}
        />
      </fieldset>

      <fieldset
        className="flex min-w-0 flex-col gap-3"
        aria-describedby={fieldErrors("kvedCodes")?.length ? "kved-codes-error" : undefined}
      >
        <legend className="text-base font-semibold">{tDeclaration("kved")}</legend>
        <p className="text-xs text-muted-foreground">{tDeclaration("kvedHint")}</p>
        {form.kvedCodes.map((code, index) => {
          const annotation = kvedAnnotation(code);

          return (
            <div key={index} className="flex min-w-0 max-w-md flex-col items-start gap-2">
              <div className="w-full min-w-0">
                <TextField
                  id={`kved-${index}`}
                  label={
                    index === 0
                      ? `${tDeclaration("kvedCode", { number: index + 1 })} (${tDeclaration("main")})`
                      : tDeclaration("kvedCode", { number: index + 1 })
                  }
                  inputMode="decimal"
                  maxLength={5}
                  placeholder="62.01"
                  value={code}
                  onChange={setKved(index)}
                  hint={annotation.hint}
                  hintClassName="break-words"
                  errors={kvedErrors(index) ?? annotation.errors}
                />
              </div>
              <Button type="button" variant="outline" onClick={() => removeKved(index)}>
                {tDeclaration("removeKved")}
              </Button>
            </div>
          );
        })}
        <LoadState query={kvedClasses} failed={tDeclaration("kvedClassesFailed")} quiet />
        <FieldErrors id="kved-codes-error" errors={fieldErrors("kvedCodes")} />
        <div>
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={form.kvedCodes.length >= maxKvedCodes}
            onClick={() => setForm((current) => ({ ...current, kvedCodes: [...current.kvedCodes, ""] }))}
          >
            {tDeclaration("addKved")}
          </Button>
        </div>
      </fieldset>

      <TextAreaField
        id="declaration-address"
        label={tDeclaration("address")}
        labelClassName="text-base font-semibold"
        hint={tDeclaration("addressHint")}
        rows={3}
        value={form.address}
        onChange={(address) => setForm((current) => ({ ...current, address }))}
        errors={fieldErrors("address")}
      />

      <fieldset className="flex min-w-0 flex-col gap-3">
        <legend className="text-base font-semibold">{tDeclaration("contacts")}</legend>
        <div className="max-w-xs">
          <TextField
            id="declaration-phone"
            label={tDeclaration("phone")}
            hint={tDeclaration("phoneHint")}
            type="tel"
            autoComplete="tel"
            placeholder="+380"
            value={form.phone}
            onChange={(phone) => setForm((current) => ({ ...current, phone }))}
            errors={fieldErrors("phone")}
          />
        </div>
        <TextField
          id="declaration-report-email"
          label={tDeclaration("reportEmail")}
          hint={tDeclaration("reportEmailHint")}
          type="email"
          autoComplete="email"
          value={form.reportEmail}
          onChange={(reportEmail) => setForm((current) => ({ ...current, reportEmail }))}
          errors={fieldErrors("reportEmail")}
        />
        {suggestedEmail ? (
          <button
            type="button"
            className="self-start break-all text-left text-sm font-medium text-primary underline-offset-4 hover:underline pointer-coarse:min-h-11"
            onClick={() => setForm((current) => ({ ...current, reportEmail: suggestedEmail }))}
          >
            {tDeclaration("reportEmailSuggest", { email: suggestedEmail })}
          </button>
        ) : null}
      </fieldset>

      {failure && !rejected ? (
        <p className="text-sm text-destructive">{apiText.withReason(t("saveFailed"), failure)}</p>
      ) : null}
      {save.isSuccess ? <p className="text-sm text-muted-foreground">{tDeclaration("saved")}</p> : null}

      <div className="flex flex-wrap items-center gap-2">
        <Button type="submit" disabled={save.isPending}>
          {save.isPending ? t("saving") : t("save")}
        </Button>
        <Button asChild variant="outline">
          <Link href="/history?entity=DeclarationDetails">{t("fop.history")}</Link>
        </Button>
      </div>
    </FieldForm>
  );
}
