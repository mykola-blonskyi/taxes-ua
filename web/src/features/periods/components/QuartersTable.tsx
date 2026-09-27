"use client";

import { useLocale, useTranslations } from "next-intl";
import { formatMoney } from "@/shared/lib/money";
import type { PeriodsResponse } from "@/data/periods/usePeriods";
import { DeadlineDate } from "./DeadlineDate";
import { PeriodTable, type PeriodColumn } from "./PeriodTable";

type Quarter = PeriodsResponse["quarters"][number];

export function QuartersTable({ quarters }: { quarters: Quarter[] }) {
  const t = useTranslations("periods");
  const locale = useLocale();

  const columns: PeriodColumn<Quarter>[] = [
    {
      key: "income",
      header: t("income"),
      numeric: true,
      cell: (quarter) => formatMoney(Number(quarter.incomeKop), locale),
    },
    {
      key: "singleTax",
      header: t("singleTax"),
      numeric: true,
      cell: (quarter) => formatMoney(Number(quarter.singleTaxKop), locale),
    },
    {
      key: "militaryLevy",
      header: t("militaryLevy"),
      numeric: true,
      cell: (quarter) => formatMoney(Number(quarter.militaryLevyKop), locale),
    },
    {
      key: "esvAmount",
      header: t("esv"),
      numeric: true,
      cell: (quarter) => formatMoney(Number(quarter.esvKop), locale),
    },
    {
      key: "total",
      header: t("total"),
      numeric: true,
      cell: (quarter) => <span className="font-semibold">{formatMoney(Number(quarter.totalKop), locale)}</span>,
    },
    {
      key: "esvDue",
      header: t("esvDue"),
      cell: (quarter) => <DeadlineDate deadline={quarter.deadlines.esv} locale={locale} />,
    },
    {
      key: "declarationDue",
      header: t("declaration"),
      cell: (quarter) => <DeadlineDate deadline={quarter.deadlines.declaration} locale={locale} />,
    },
    {
      key: "taxPaymentDue",
      header: t("taxPayment"),
      cell: (quarter) => <DeadlineDate deadline={quarter.deadlines.taxPayment} locale={locale} />,
    },
  ];

  return (
    <PeriodTable
      rows={quarters}
      columns={columns}
      rowKey={(quarter) => String(quarter.quarter)}
      rowHeader={(quarter) => t("quarterNumber", { quarter: quarter.quarter })}
      rowHeaderLabel={t("quarter")}
    />
  );
}
