"use client";

import { useTranslations } from "next-intl";
import type { KindDebt } from "@/data/dashboard/useDashboard";
import { cn } from "@/shared/lib/utils";
import { periodOf } from "./debt";

export function DebtPeriod({ debt }: { debt: KindDebt }) {
  const t = useTranslations("dashboard");
  const { fromYear, fromQuarter, toYear, toQuarter } = periodOf(debt);

  if (fromYear !== toYear) {
    return <>{t("quarterSpan", { fromQuarter, fromYear, toQuarter, toYear })}</>;
  }

  return (
    <>
      {fromQuarter === toQuarter
        ? t("quarter", { quarter: toQuarter, year: toYear })
        : t("quarterRange", { from: fromQuarter, to: toQuarter, year: toYear })}
    </>
  );
}

export function DaysLeft({ days, className }: { days: number; className?: string }) {
  const t = useTranslations("dashboard.days");

  if (days < 0) {
    return <span className={cn("text-destructive", className)}>{t("overdue", { days: -days })}</span>;
  }

  if (days === 0) {
    return <span className={cn("text-amber-700 dark:text-amber-400", className)}>{t("today")}</span>;
  }

  return <span className={cn("text-muted-foreground", className)}>{t("left", { days })}</span>;
}
