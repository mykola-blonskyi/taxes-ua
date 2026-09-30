"use client";

import { useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import {
  useDeclarationDetails,
  useSaveDeclarationDetails,
  type DeclarationDetailsResponse,
} from "@/data/declarations/useDeclarations";
import { Button } from "@/shared/ui/button";
import { TextAreaField, TextField } from "@/shared/ui/fields";

const maxKvedCodes = 20;

type FormState = { region: string; district: string; kvedCodes: string[]; address: string };

type ErrorKey =
  | "regionRange"
  | "districtRange"
  | "bothParts"
  | "kvedFormat"
  | "kvedDuplicate"
  | "kvedTooMany"
  | "tooLong"
  | "controlChar";

// The api answers in English from a closed set of messages; each maps to a translated one and anything
// unforeseen is shown as it came.
const errorPatterns: readonly (readonly [string, ErrorKey])[] = [
  ["1 to 99", "regionRange"],
  ["0 to 99", "districtRange"],
  ["is required with", "bothParts"],
  ["two digits, a dot", "kvedFormat"],
  ["must not repeat", "kvedDuplicate"],
  ["must not list more", "kvedTooMany"],
  ["exceed", "tooLong"],
  ["control character", "controlChar"],
];

function twoDigits(value: number | string | null): string {
  return value === null ? "" : String(value).padStart(2, "0");
}

function toFormState(details: DeclarationDetailsResponse): FormState {
  return {
    region: twoDigits(details.taxOfficeRegion),
    district: twoDigits(details.taxOfficeDistrict),
    kvedCodes: details.kvedCodes.length > 0 ? details.kvedCodes : [""],
    address: details.address,
  };
}

export function DeclarationDetailsForm() {
  const t = useTranslations("settings");
  const { data, isLoading, isError } = useDeclarationDetails();

  if (isLoading) {
    return <p className="text-sm text-muted-foreground">{t("loading")}</p>;
  }

  if (isError || !data) {
    return <p className="text-sm text-destructive">{t("loadFailed")}</p>;
  }

  return <DeclarationDetailsFormBody details={data} />;
}

function DeclarationDetailsFormBody({ details }: { details: DeclarationDetailsResponse }) {
  const t = useTranslations("settings");
  const tDeclaration = useTranslations("settings.declaration");
  const tFields = useTranslations("declaration.fields");
  const save = useSaveDeclarationDetails();
  const [form, setForm] = useState<FormState>(() => toFormState(details));

  const failure = save.error instanceof ApiError ? save.error : null;
  const rejected = Object.keys(failure?.errors ?? {}).length > 0;
  const missing = details.missingDetails;
  const sentKved = form.kvedCodes.flatMap((code, index) => (code.trim() === "" ? [] : [index]));

  function fieldErrors(key: string): string[] | undefined {
    return failure?.errors[key]?.map((message) => {
      const match = errorPatterns.find(([pattern]) => message.includes(pattern));

      return match ? tDeclaration(`errors.${match[1]}`) : message;
    });
  }

  function kvedErrors(index: number): string[] | undefined {
    const sent = sentKved.indexOf(index);

    return sent < 0 ? undefined : fieldErrors(`kvedCodes[${sent}]`);
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
    <form
      className="flex min-w-0 flex-col gap-6"
      onSubmit={(event) => {
        event.preventDefault();
        save.mutate({
          taxOfficeRegion: form.region === "" ? null : Number(form.region),
          taxOfficeDistrict: form.district === "" ? null : Number(form.district),
          kvedCodes: sentKved.map((index) => form.kvedCodes[index].trim()),
          address: form.address,
        });
      }}
    >
      {missing.length > 0 ? (
        <p className="rounded-lg border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-800 dark:text-amber-300">
          {tDeclaration("missing", { fields: missing.map((field) => tFields(field)).join(", ") })}
        </p>
      ) : (
        <p className="text-sm text-emerald-700 dark:text-emerald-400">{tDeclaration("complete")}</p>
      )}

      {rejected ? (
        <p className="rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">
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
      </fieldset>

      <fieldset className="flex min-w-0 flex-col gap-3">
        <legend className="text-base font-semibold">{tDeclaration("kved")}</legend>
        <p className="text-xs text-muted-foreground">{tDeclaration("kvedHint")}</p>
        {form.kvedCodes.map((code, index) => (
          <div key={index} className="flex min-w-0 max-w-xs items-end gap-2">
            <div className="min-w-0 flex-1">
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
                errors={kvedErrors(index)}
              />
            </div>
            <Button type="button" variant="outline" onClick={() => removeKved(index)}>
              {tDeclaration("removeKved")}
            </Button>
          </div>
        ))}
        {fieldErrors("kvedCodes")?.map((message) => (
          <p key={message} className="text-xs text-destructive">
            {message}
          </p>
        ))}
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

      {failure && !rejected ? (
        <p className="text-sm text-destructive">{`${t("saveFailed")} ${failure.message}`}</p>
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
    </form>
  );
}
