"use client";

import { SquareCheck } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import type { CabinetField, DeclarationResponse } from "@/data/declarations/useDeclarations";
import { formatDateOnly } from "@/shared/lib/dates";
import { CopyChip, CopyField } from "@/shared/ui/copy-field";
import { ProvisionalNote } from "./ProvisionalNote";

type Rate = "singleTax" | "excess" | "militaryLevy";

// The rate in the label of the lines that print one; the other lines' labels have no placeholder.
const lineRates: Record<string, Rate> = {
  "06": "singleTax",
  "07": "excess",
  "09": "excess",
  "11": "singleTax",
  "23": "militaryLevy",
};

function formatLabelRate(basisPoints: number, locale: string): string {
  return new Intl.NumberFormat(locale, { style: "percent", maximumFractionDigits: 2 }).format(basisPoints / 10000);
}

// "14.1" is the key l14_1: the dot cannot sit in a message key.
const lineKey = (line: string) => `l${line.replace(".", "_")}` as const;

export function FillInCabinet({ declaration }: { declaration: DeclarationResponse }) {
  const t = useTranslations("declaration.cabinet");
  const tFigures = useTranslations("declaration.figures");
  const tProvisional = useTranslations("declaration.provisional");
  const locale = useLocale();
  const { figures, limitCrossing, cabinet } = declaration;
  const year = Number(declaration.year);
  const quarter = Number(declaration.quarter);
  const crossedHere =
    limitCrossing !== null && Number(limitCrossing.year) === year && Number(limitCrossing.quarter) === quarter;
  const rates: Record<Rate, string> = {
    singleTax: formatLabelRate(Number(declaration.singleTaxRateBp), locale),
    excess: formatLabelRate(Number(declaration.excessRateBp), locale),
    militaryLevy: formatLabelRate(Number(declaration.militaryLevyRateBp), locale),
  };
  const period = t("periodName", { quarter });
  const hasAnnex = cabinet.some((field) => field.part === "Annex");
  const ready = declaration.readiness.ready;
  const monthName = (month: number) => {
    const name = new Intl.DateTimeFormat(locale, { month: "long", timeZone: "UTC" }).format(new Date(Date.UTC(2000, month - 1, 1)));

    return name.charAt(0).toLocaleUpperCase(locale) + name.slice(1);
  };

  function labelOf(field: CabinetField): string {
    const { element, line } = field;
    const month = Number(field.month);
    if (month > 0) {
      return t("fields.monthCell", {
        month: monthName(month),
        column: t(`fields.column${Number(field.column)}` as "fields.column2"),
      });
    }

    if (field.part === "Declaration" && line !== null) {
      const rate = lineRates[line];
      const text = rate ? tFigures(lineKey(line) as "l06", { rate: rates[rate] }) : tFigures(lineKey(line) as "l08");

      return `${line}. ${text}`;
    }

    if (element === "T1RXXXXG1S") {
      const count = cabinet.filter((other) => other.element === element).length;

      return count > 1 ? t("fields.kvedRow", { row: Number(field.row) }) : t("fields.kved");
    }

    if (field.part === "Period" && field.kind === "Mark") {
      return t("fields.period", { period });
    }

    return t(`fields.${element}` as "fields.HSTI");
  }

  function copyLabelOf(field: CabinetField): string {
    return t("copy", { field: field.line !== null && field.part === "Declaration" ? t("line", { line: field.line }) : labelOf(field) });
  }

  function renderField(field: CabinetField) {
    const key = `${field.element}-${Number(field.row)}-${Number(field.month)}-${Number(field.column)}`;
    if (field.kind === "Mark") {
      return (
        <li key={key} className="flex min-w-0 items-start gap-2 text-sm">
          <SquareCheck className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
          <span className="min-w-0 break-words">{t("tick", { label: labelOf(field) })}</span>
        </li>
      );
    }

    if (field.value === null) {
      return null;
    }

    return (
      <li key={key} className="min-w-0">
        {ready ? <CopyField
          label={labelOf(field)}
          value={field.value}
          copyLabel={copyLabelOf(field)}
          copiedLabel={t("copied")}
          failedLabel={t("copyFailed")}
        /> : (
          <div className="flex min-w-0 flex-col gap-0.5">
            <span className="text-xs text-muted-foreground">{labelOf(field)}</span>
            <span className="text-sm font-medium wrap-anywhere">{field.value}</span>
          </div>
        )}
      </li>
    );
  }

  function renderMonth(month: number, cells: CabinetField[]) {
    return (
      <li key={`month-${month}`} className="flex min-w-0 flex-col gap-1">
        <span className="text-xs text-muted-foreground">{monthName(month)}</span>
        <div className="grid grid-cols-3 gap-2">
          {cells.map((cell) =>
            ready ? (
              <CopyChip
                key={cell.element}
                label={t(`fields.short${Number(cell.column)}` as "fields.short2")}
                value={cell.value ?? ""}
                copyLabel={t("copy", { field: labelOf(cell) })}
                copiedLabel={t("copied")}
                failedLabel={t("copyFailed")}
              />
            ) : (
              <div key={cell.element} className="flex min-w-0 flex-col gap-0.5">
                <span className="text-xs text-muted-foreground">{t(`fields.short${Number(cell.column)}` as "fields.short2")}</span>
                <span className="text-sm font-medium wrap-anywhere">{cell.value}</span>
              </div>
            ),
          )}
        </div>
      </li>
    );
  }

  const section = (part: CabinetField["part"], title: string) => {
    const fields = cabinet.filter((field) => field.part === part);

    return fields.length === 0 ? null : (
      <div key={part} className="flex min-w-0 flex-col gap-2">
        <h4 className="text-sm font-medium">{title}</h4>
        <ul className="flex min-w-0 flex-col gap-3 rounded-lg border bg-card p-3">
          {fields.map((field) => {
            const month = Number(field.month);
            if (month === 0) {
              return renderField(field);
            }

            return Number(field.column) === 2
              ? renderMonth(month, fields.filter((cell) => Number(cell.month) === month))
              : null;
          })}
        </ul>
      </div>
    );
  };

  const emptyLines = cabinet
    .filter((field) => field.part === "Declaration" && field.value === null && field.line !== null)
    .map((field) => field.line);

  return (
    <section className="flex min-w-0 flex-col gap-3" aria-labelledby="cabinet-heading">
      <div className="flex flex-col gap-1">
        <h3 id="cabinet-heading" className="text-base font-semibold">
          {t("title")}
        </h3>
        <p className="text-xs text-muted-foreground">{t("hint")}</p>
      </div>
      {limitCrossing !== null && crossedHere ? (
        <p className="break-words rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">
          {tFigures("crossing", {
            switchQuarter: Number(limitCrossing.switchFromQuarter),
            switchYear: Number(limitCrossing.switchFromYear),
          })}
        </p>
      ) : null}
      {figures === null ? (
        <p className="break-words text-sm text-destructive">
          {limitCrossing
            ? tFigures("afterGroup3", {
                quarter: Number(limitCrossing.quarter),
                year: Number(limitCrossing.year),
                switchQuarter: Number(limitCrossing.switchFromQuarter),
                switchYear: Number(limitCrossing.switchFromYear),
              })
            : tFigures("afterGroup3Plain")}
        </p>
      ) : (
        <>
          {declaration.readiness.group3Confirmed ? null : <ProvisionalNote text={tProvisional("cabinet")} />}
          {ready ? null : (
            <p className="break-words rounded-lg border bg-card p-3 text-sm">{t("notFinal")}</p>
          )}
          {declaration.fileAvailable ? null : (
            <p className="break-words rounded-lg border bg-card p-3 text-sm">
              {t("notEnded", { date: formatDateOnly(declaration.fileAvailableFrom, locale) })}
            </p>
          )}

          <div className="flex min-w-0 flex-col gap-2">
            <h4 className="text-sm font-medium">{t("guide.title")}</h4>
            <ol className="flex list-decimal flex-col gap-1 pl-5 text-sm">
              <li>{t("guide.open")}</li>
              <li>{t("guide.form")}</li>
              <li>{t("guide.period", { period, year })}</li>
              <li>{t("guide.fill")}</li>
              {hasAnnex ? <li>{t("guide.annex")}</li> : null}
              <li>{t("guide.check")}</li>
              <li>{t("guide.sign")}</li>
            </ol>
            <p className="text-xs text-muted-foreground">{t("guide.noSend")}</p>
            <p className="text-xs text-muted-foreground">{t("format")}</p>
          </div>

          {cabinet.some((field) => field.part === "Header") ? null : (
            <p className="text-xs text-muted-foreground">{t("headerMissing")}</p>
          )}
          {section("Period", t("sections.period"))}
          {section("Header", t("sections.header"))}
          {section("Declaration", t("sections.lines"))}
          <p className="text-sm text-muted-foreground">
            {emptyLines.length > 0 ? `${t("empty", { count: emptyLines.length, lines: emptyLines.join(", ") })} ` : null}
            {t("othersEmpty")}
          </p>
          {section("Annex", t("sections.annex"))}
        </>
      )}
    </section>
  );
}
