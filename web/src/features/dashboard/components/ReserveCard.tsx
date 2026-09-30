"use client";

import { useLocale, useTranslations } from "next-intl";
import type { DashboardResponse, Reserve } from "@/data/dashboard/useDashboard";
import { formatMoney } from "@/shared/lib/money";
import { formatLongDate } from "./debt";
import { DaysLeft } from "./DebtParts";

const kinds = [
  ["SingleTax", "singleTaxKop"],
  ["MilitaryLevy", "militaryLevyKop"],
  ["Esv", "esvKop"],
] as const;

export function ReserveCard({
  reserve,
  today,
  limitCrossing,
}: {
  reserve: Reserve;
  today: string;
  limitCrossing: DashboardResponse["limitCrossing"];
}) {
  const t = useTranslations("dashboard.reserve");
  const tKinds = useTranslations("payments.kinds");
  const locale = useLocale();

  return (
    <section className="flex flex-col gap-2 rounded-xl border bg-card p-4 md:p-6">
      <h3 className="text-base font-semibold">{t("title")}</h3>
      <p className="text-3xl font-semibold tabular-nums">{formatMoney(Number(reserve.totalKop), locale)}</p>
      {reserve.dues.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t("empty")}</p>
      ) : (
        <ul className="flex flex-col divide-y rounded-lg border">
          {reserve.dues.map((due) => (
            <li key={due.dueDate} className="flex flex-col gap-1 p-3">
              <div className="flex flex-wrap items-baseline justify-between gap-x-4">
                <span className="text-sm font-medium">{t("by", { date: formatLongDate(due.dueDate, today, locale) })}</span>
                <span className="text-xs">
                  <DaysLeft days={Number(due.daysLeft)} />
                </span>
              </div>
              {kinds
                .filter(([, field]) => Number(due[field]) > 0)
                .map(([kind, field]) => (
                  <div key={kind} className="grid grid-cols-[minmax(0,1fr)_auto] items-baseline gap-x-4 text-sm">
                    <span className="text-muted-foreground">{tKinds(kind)}</span>
                    <span className="font-semibold tabular-nums">{formatMoney(Number(due[field]), locale)}</span>
                  </div>
                ))}
            </li>
          ))}
        </ul>
      )}
      <p className="text-xs text-muted-foreground">{t("hint")}</p>
      {limitCrossing ? (
        <p className="break-words text-xs text-destructive">
          {t("crossingNote", {
            switchQuarter: Number(limitCrossing.switchFromQuarter),
            switchYear: Number(limitCrossing.switchFromYear),
          })}
        </p>
      ) : null}
    </section>
  );
}
