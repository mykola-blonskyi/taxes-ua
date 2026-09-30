"use client";

import { useLocale, useTranslations } from "next-intl";
import type { DeclarationFigures, DeclarationResponse } from "@/data/declarations/useDeclarations";
import { formatMoney } from "@/shared/lib/money";

type LineKey = "l06" | "l07" | "l08" | "l09" | "l11" | "l12" | "l13" | "l14_1" | "l14" | "l21" | "l23" | "l24" | "l25";

type Line = { key: LineKey; code: string; kop: (figures: DeclarationFigures) => number | null; rate?: "singleTax" | "excess" | "militaryLevy" };

const nonzero = (kop: number | string): number | null => (Number(kop) === 0 ? null : Number(kop));

// The form's own order. 07 and 09 are empty on the form unless the year's income crossed the limit in this
// quarter; 14.1 and 14 carry the same figure because group 3 has nothing else in those lines.
const lines: readonly Line[] = [
  { key: "l06", code: "06", kop: (f) => Number(f.incomeKop), rate: "singleTax" },
  { key: "l07", code: "07", kop: (f) => nonzero(f.excessIncomeKop), rate: "excess" },
  { key: "l08", code: "08", kop: (f) => Number(f.totalIncomeKop) },
  { key: "l09", code: "09", kop: (f) => nonzero(f.excessTaxKop), rate: "excess" },
  { key: "l11", code: "11", kop: (f) => Number(f.singleTaxKop), rate: "singleTax" },
  { key: "l12", code: "12", kop: (f) => Number(f.totalSingleTaxKop) },
  { key: "l13", code: "13", kop: (f) => Number(f.previousSingleTaxKop) },
  { key: "l14_1", code: "14.1", kop: (f) => Number(f.singleTaxPayableKop) },
  { key: "l14", code: "14", kop: (f) => Number(f.singleTaxPayableKop) },
  { key: "l21", code: "21", kop: (f) => (f.esvKop === null ? null : Number(f.esvKop)) },
  { key: "l23", code: "23", kop: (f) => Number(f.militaryLevyKop), rate: "militaryLevy" },
  { key: "l24", code: "24", kop: (f) => Number(f.previousMilitaryLevyKop) },
  { key: "l25", code: "25", kop: (f) => Number(f.militaryLevyPayableKop) },
];

function formatLabelRate(basisPoints: number, locale: string): string {
  return new Intl.NumberFormat(locale, { style: "percent", maximumFractionDigits: 2 }).format(basisPoints / 10000);
}

export function Figures({ declaration }: { declaration: DeclarationResponse }) {
  const t = useTranslations("declaration.figures");
  const locale = useLocale();
  const { figures, limitCrossing } = declaration;
  const crossedHere =
    limitCrossing !== null &&
    Number(limitCrossing.year) === Number(declaration.year) &&
    Number(limitCrossing.quarter) === Number(declaration.quarter);
  const rates = {
    singleTax: formatLabelRate(Number(declaration.singleTaxRateBp), locale),
    excess: formatLabelRate(Number(declaration.excessRateBp), locale),
    militaryLevy: formatLabelRate(Number(declaration.militaryLevyRateBp), locale),
  };

  return (
    <section className="flex min-w-0 flex-col gap-3" aria-labelledby="figures-heading">
      <div className="flex flex-col gap-1">
        <h3 id="figures-heading" className="text-base font-semibold">
          {t("title")}
        </h3>
        <p className="text-xs text-muted-foreground">{t("hint")}</p>
      </div>
      {limitCrossing !== null && crossedHere ? (
        <p className="break-words rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive">
          {t("crossing", {
            switchQuarter: Number(limitCrossing.switchFromQuarter),
            switchYear: Number(limitCrossing.switchFromYear),
          })}
        </p>
      ) : null}
      {figures === null ? (
        <p className="break-words text-sm text-destructive">
          {limitCrossing
            ? t("afterGroup3", {
                quarter: Number(limitCrossing.quarter),
                year: Number(limitCrossing.year),
                switchQuarter: Number(limitCrossing.switchFromQuarter),
                switchYear: Number(limitCrossing.switchFromYear),
              })
            : t("afterGroup3Plain")}
        </p>
      ) : (
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b text-left text-xs text-muted-foreground">
              <th scope="col" className="py-2 pr-3 font-medium">
                {t("line")}
              </th>
              <th scope="col" className="py-2 pr-3 font-medium">
                {t("name")}
              </th>
              <th scope="col" className="py-2 text-right font-medium">
                {t("amount")}
              </th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {lines.map((line) => {
              const kop = line.kop(figures);

              return kop === null ? null : (
                <tr key={line.key} className="align-baseline">
                  <th scope="row" className="py-2 pr-3 text-left font-medium tabular-nums">
                    {line.code}
                  </th>
                  <td className="break-words py-2 pr-3">
                    {line.rate ? t(line.key, { rate: rates[line.rate] }) : t(line.key)}
                  </td>
                  <td className="whitespace-nowrap py-2 text-right tabular-nums">{formatMoney(kop, locale)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </section>
  );
}
