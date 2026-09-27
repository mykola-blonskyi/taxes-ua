"use client";

import { useLocale, useTranslations } from "next-intl";
import { formatMoney } from "@/shared/lib/money";
import type { PeriodsResponse } from "@/data/periods/usePeriods";
import { formatDate } from "./DeadlineDate";
import { PeriodTable, type PeriodColumn } from "./PeriodTable";

type Month = NonNullable<PeriodsResponse["months"]>[number];

function monthName(month: number, locale: string): string {
  const name = new Intl.DateTimeFormat(locale, {
    month: "long",
    timeZone: "UTC",
  }).format(new Date(Date.UTC(2000, month - 1, 1)));

  return name.charAt(0).toLocaleUpperCase(locale) + name.slice(1);
}

// The EP, VZ and ESV columns are what the month accrued; the advance is what of them is still unpaid
// after allocation, so a paid month reads as paid rather than repeating its accrual.
export function MonthsTable({ months }: { months: Month[] }) {
  const t = useTranslations("periods");
  const locale = useLocale();

  const columns: PeriodColumn<Month>[] = [
    {
      key: "income",
      header: t("income"),
      numeric: true,
      cell: (month) => formatMoney(Number(month.incomeKop), locale),
    },
    {
      key: "singleTax",
      header: t("singleTax"),
      numeric: true,
      cell: (month) => formatMoney(Number(month.singleTaxKop), locale),
    },
    {
      key: "militaryLevy",
      header: t("militaryLevy"),
      numeric: true,
      cell: (month) => formatMoney(Number(month.militaryLevyKop), locale),
    },
    {
      key: "esv",
      header: t("esv"),
      numeric: true,
      cell: (month) => formatMoney(Number(month.esvKop), locale),
    },
    {
      key: "advance",
      header: t("advance"),
      numeric: true,
      cell: (month) => {
        if (Number(month.recommendedKop) > 0) {
          return (
            <span className="flex flex-col items-end">
              <span className="font-semibold">{formatMoney(Number(month.recommendedKop), locale)}</span>
              <span className="text-xs text-muted-foreground">
                {t("advanceBy", {
                  date: formatDate(month.recommendedDate, locale),
                })}
              </span>
            </span>
          );
        }

        const accrued = [month.singleTaxKop, month.militaryLevyKop, month.esvKop].some((kop) => Number(kop) > 0);
        return <span className="text-muted-foreground">{accrued ? t("advancePaid") : "—"}</span>;
      },
    },
  ];

  return (
    <PeriodTable
      rows={months}
      columns={columns}
      rowKey={(month) => String(month.month)}
      rowHeader={(month) => monthName(Number(month.month), locale)}
      rowHeaderLabel={t("month")}
    />
  );
}
