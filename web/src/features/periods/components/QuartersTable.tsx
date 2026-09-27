"use client";

import type { ReactNode } from "react";
import { useLocale, useTranslations } from "next-intl";
import { formatMoney } from "@/shared/lib/money";
import type { PeriodsResponse } from "@/data/periods/usePeriods";
import { DeadlineDate } from "./DeadlineDate";
import { PeriodTable, type PeriodColumn } from "./PeriodTable";

type Quarter = PeriodsResponse["quarters"][number];
type Obligations = NonNullable<Quarter["obligations"]>;
type Obligation = Obligations["singleTax"];

const kinds = ["singleTax", "militaryLevy", "esv"] as const satisfies (keyof Obligations)[];

// A zero balance says Done by itself; a negative one is labelled as an overpayment, as the panel does.
const statusClass: Record<Exclude<Obligation["status"], "Done">, string> = {
  Upcoming: "text-muted-foreground",
  Due: "text-destructive",
  Overdue: "text-destructive",
};

// Rule 7 keeps the three kinds apart, so a quarter's paid and remaining are three lines, never one sum.
function PerKind({
  obligations,
  render,
  label,
}: {
  obligations: Quarter["obligations"];
  render: (obligation: Obligation) => ReactNode;
  label: (kind: (typeof kinds)[number]) => string;
}) {
  if (!obligations) {
    return <span className="text-muted-foreground">—</span>;
  }

  return (
    <div className="flex flex-col items-end gap-0.5">
      {kinds.map((kind) => (
        <div key={kind} className="flex items-baseline justify-end gap-x-1.5 whitespace-nowrap">
          <span className="text-xs text-muted-foreground">{label(kind)}</span>
          {render(obligations[kind])}
        </div>
      ))}
    </div>
  );
}

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
      key: "paid",
      header: t("paid"),
      numeric: true,
      cell: (quarter) => (
        <PerKind
          obligations={quarter.obligations}
          label={(kind) => t(kind)}
          render={(obligation) => <span>{formatMoney(Number(obligation.paidKop), locale)}</span>}
        />
      ),
    },
    {
      key: "remaining",
      header: t("remaining"),
      numeric: true,
      cell: (quarter) => (
        <PerKind
          obligations={quarter.obligations}
          label={(kind) => t(kind)}
          render={(obligation) => (
            <span className="flex flex-col items-end">
              <span className={obligation.status === "Done" ? "text-muted-foreground" : "font-medium"}>
                {formatMoney(Number(obligation.remainingKop), locale)}
              </span>
              {obligation.status !== "Done" ? (
                <span className={`text-xs ${statusClass[obligation.status]}`}>{t(`status.${obligation.status}`)}</span>
              ) : null}
            </span>
          )}
        />
      ),
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
