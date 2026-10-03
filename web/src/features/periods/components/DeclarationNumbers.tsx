"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { declarationHref } from "@/shared/constants/navigation";
import { formatMoney } from "@/shared/lib/money";
import type { PeriodsResponse } from "@/data/periods/usePeriods";
import { DeadlineDate } from "@/shared/ui/DeadlineDate";
import { PeriodTable, type PeriodColumn } from "./PeriodTable";

type Quarter = PeriodsResponse["quarters"][number];

function declarationPeriodLabel(t: ReturnType<typeof useTranslations<"periods">>, quarter: number): string {
  switch (quarter) {
    case 1:
      return t("declarationPeriod.q1");
    case 2:
      return t("declarationPeriod.q2");
    case 3:
      return t("declarationPeriod.q3");
    default:
      return t("declarationPeriod.q4");
  }
}

export function DeclarationNumbers({ year, quarters }: { year: number; quarters: Quarter[] }) {
  const t = useTranslations("periods");
  const locale = useLocale();

  const columns: PeriodColumn<Quarter>[] = [
    {
      key: "cumulativeIncome",
      header: t("income"),
      numeric: true,
      cell: (quarter) => formatMoney(Number(quarter.cumulativeIncomeKop), locale),
    },
    {
      key: "cumulativeSingleTax",
      header: t("singleTax"),
      numeric: true,
      cell: (quarter) => (
        <>
          {formatMoney(Number(quarter.cumulativeSingleTaxKop), locale)}
          {Number(quarter.cumulativeExcessIncomeKop) > 0 ? (
            <span className="block whitespace-normal break-words text-xs font-normal text-muted-foreground">
              {t("excessNote", {
                income: formatMoney(Number(quarter.cumulativeExcessIncomeKop), locale),
                tax: formatMoney(Number(quarter.cumulativeExcessTaxKop), locale),
              })}
            </span>
          ) : null}
        </>
      ),
    },
    {
      key: "cumulativeMilitaryLevy",
      header: t("militaryLevy"),
      numeric: true,
      cell: (quarter) => formatMoney(Number(quarter.cumulativeMilitaryLevyKop), locale),
    },
    {
      key: "filingDeadline",
      header: t("filingDeadline"),
      cell: (quarter) => <DeadlineDate deadline={quarter.deadlines.declaration} locale={locale} />,
    },
  ];

  return (
    <PeriodTable
      rows={quarters}
      columns={columns}
      rowKey={(quarter) => String(quarter.quarter)}
      rowHeader={(quarter) => (
        <Link
          href={declarationHref(year, Number(quarter.quarter))}
          className="text-primary underline-offset-4 hover:underline pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center"
        >
          {declarationPeriodLabel(t, Number(quarter.quarter))}
        </Link>
      )}
      rowHeaderLabel={t("period")}
    />
  );
}
