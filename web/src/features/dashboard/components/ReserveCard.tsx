"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import type { DashboardResponse, Reserve } from "@/data/dashboard/useDashboard";
import { jarErrorKind, useRefreshReserveJar } from "@/data/monobank/useReserveJar";
import { formatInstantInKyiv } from "@/shared/lib/dates";
import { formatMoney } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";
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
  const tJar = useTranslations("reserveJar");
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
      {reserve.jar ? (
        <JarCover jar={reserve.jar} today={today} />
      ) : reserve.canChooseJar ? (
        <Link href="/settings?tab=monobank" className="text-sm text-primary underline-offset-4 hover:underline pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center">
          {tJar("card.choose")}
        </Link>
      ) : null}
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

function JarCover({ jar, today }: { jar: NonNullable<Reserve["jar"]>; today: string }) {
  const t = useTranslations("reserveJar");
  const locale = useLocale();
  const refresh = useRefreshReserveJar();
  const shortfall = Number(jar.shortfallKop);
  const topUp = Number(jar.topUpKop);
  const surplus = Number(jar.surplusKop);
  const time = formatInstantInKyiv(jar.fetchedAt, locale);

  return (
    <div className="flex min-w-0 flex-col gap-1 rounded-lg border p-3">
      <div className="grid grid-cols-[minmax(0,1fr)_auto] items-baseline gap-x-4 text-sm">
        <span className="min-w-0 break-words text-muted-foreground">{t("card.title", { title: jar.title })}</span>
        <span className="font-semibold tabular-nums">{formatMoney(Number(jar.balanceKop), locale)}</span>
      </div>
      {shortfall > 0 && jar.topUpBy ? (
        <>
          <p className="text-sm font-semibold text-destructive">
            {t("card.topUp", { amount: formatMoney(topUp, locale), date: formatLongDate(jar.topUpBy, today, locale) })}
          </p>
          {shortfall > topUp ? (
            <p className="text-xs text-muted-foreground">{t("card.shortfallTotal", { amount: formatMoney(shortfall, locale) })}</p>
          ) : null}
        </>
      ) : (
        <p className="text-sm font-semibold text-emerald-700 dark:text-emerald-400">
          {surplus > 0 ? t("card.covered", { amount: formatMoney(surplus, locale) }) : t("card.coveredExactly")}
        </p>
      )}
      <p className={jar.stale ? "text-xs text-destructive" : "text-xs text-muted-foreground"}>
        {jar.stale ? t("stale", { time }) : t("asOf", { time })}
      </p>
      <div className="flex flex-wrap items-center gap-2">
        <Button type="button" variant="outline" size="sm" disabled={refresh.isPending} onClick={() => refresh.mutate()}>
          {refresh.isPending ? t("refreshing") : t("refresh")}
        </Button>
        {refresh.isError ? (
          <span className="min-w-0 break-words text-xs text-destructive">{t(`errors.${jarErrorKind(refresh.error)}`)}</span>
        ) : null}
      </div>
    </div>
  );
}
