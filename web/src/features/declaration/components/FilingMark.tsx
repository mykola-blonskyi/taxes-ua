"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { problemOf } from "@/data/api/client";
import { useApiErrorText } from "@/data/api/useApiErrorText";
import {
  useMarkDeclarationFiled,
  useUndoDeclarationFiling,
  type DeclarationFiling,
  type DeclarationType,
} from "@/data/declarations/useDeclarations";
import { formatNumericDate } from "@/shared/lib/dates";
import { formatMoney } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";
import { SelectField, TextField } from "@/shared/ui/fields";
import { dayAfterQuarter, type Period } from "../period";
import { ProvisionalNote } from "./ProvisionalNote";

const declarationTypes = ["Reporting", "NewReporting", "Clarifying"] as const satisfies readonly DeclarationType[];

export function FilingMark({
  filed,
  period,
  today,
  group3Confirmed,
}: {
  filed: DeclarationFiling | null;
  period: Period;
  today: string;
  group3Confirmed: boolean;
}) {
  const t = useTranslations("declaration.filing");
  const tProvisional = useTranslations("declaration.provisional");

  return (
    <section className="flex min-w-0 flex-col gap-3" aria-labelledby="filing-heading">
      <div className="flex flex-col gap-1">
        <h3 id="filing-heading" className="text-base font-semibold">
          {t("title")}
        </h3>
        <p className="text-xs text-muted-foreground">{t("hint")}</p>
      </div>
      {group3Confirmed ? null : <ProvisionalNote text={tProvisional("filing")} />}
      {filed ? <Filed filed={filed} period={period} /> : <MarkForm period={period} today={today} />}
    </section>
  );
}

function Filed({ filed, period }: { filed: DeclarationFiling; period: Period }) {
  const t = useTranslations("declaration.filing");
  const tTypes = useTranslations("declaration.types");
  const locale = useLocale();
  const apiText = useApiErrorText();
  const undo = useUndoDeclarationFiling(period.year, period.quarter);

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <p className="text-sm font-medium text-emerald-700 dark:text-emerald-400">
          {t("filed", { date: formatNumericDate(filed.filedOn, locale), type: tTypes(filed.type) })}
        </p>
        <Button type="button" variant="outline" size="sm" disabled={undo.isPending} onClick={() => undo.mutate()}>
          {t("undo")}
        </Button>
      </div>
      {filed.changedSinceFiling ? (
        <p className="rounded-lg border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-800 dark:text-amber-300">
          {t("changedSinceFiling", { filed: formatMoney(Number(filed.filedIncomeKop), locale) })}
        </p>
      ) : null}
      {undo.isError ? <p className="text-sm text-destructive">{apiText.withReason(t("undoFailed"), undo.error)}</p> : null}
    </div>
  );
}

function MarkForm({ period, today }: { period: Period; today: string }) {
  const t = useTranslations("declaration.filing");
  const tTypes = useTranslations("declaration.types");
  const apiText = useApiErrorText();
  const mark = useMarkDeclarationFiled(period.year, period.quarter);
  const [filedOn, setFiledOn] = useState(today);
  const [type, setType] = useState<DeclarationType>("Reporting");

  const dateRejected = problemOf(mark.error)?.fieldCodes.filedOn !== undefined;

  return (
    <form
      className="flex min-w-0 flex-col gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        mark.mutate({ filedOn, type });
      }}
    >
      <div className="grid min-w-0 gap-3 sm:grid-cols-2">
        <TextField
          id="filed-on"
          type="date"
          label={t("date")}
          value={filedOn}
          min={dayAfterQuarter(period)}
          max={today}
          required
          onChange={setFiledOn}
          errors={dateRejected ? [t("dateError")] : undefined}
        />
        <SelectField
          id="declaration-type"
          label={t("type")}
          value={type}
          onChange={(value) => setType(value as DeclarationType)}
          options={declarationTypes.map((value) => ({ value, label: tTypes(value) }))}
        />
      </div>
      {mark.isError && !dateRejected ? <p className="text-sm text-destructive">{apiText.withReason(t("markFailed"), mark.error)}</p> : null}
      <div>
        <Button type="submit" disabled={mark.isPending}>
          {mark.isPending ? t("marking") : t("mark")}
        </Button>
      </div>
    </form>
  );
}
