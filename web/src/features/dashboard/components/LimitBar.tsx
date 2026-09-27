"use client";

import { useLocale, useTranslations } from "next-intl";
import type { LimitStatus } from "@/data/dashboard/useDashboard";
import { formatMoney } from "@/shared/lib/money";
import { cn } from "@/shared/lib/utils";

const toneByLevel = {
  Ok: { bar: "bg-emerald-600 dark:bg-emerald-500", text: "text-muted-foreground" },
  Warn: { bar: "bg-amber-500 dark:bg-amber-400", text: "text-amber-700 dark:text-amber-400" },
  Exceeded: { bar: "bg-destructive", text: "text-destructive" },
} as const;

export function LimitBar({ limit }: { limit: LimitStatus }) {
  const t = useTranslations("dashboard.limit");
  const locale = useLocale();
  const tone = toneByLevel[limit.level];
  const fillPercent = Math.min(100, Number(limit.percentBp) / 100);

  return (
    <section className="flex flex-col gap-2">
      <h3 className="text-base font-semibold">{t("title")}</h3>
      <div
        role="progressbar"
        aria-valuenow={fillPercent}
        aria-valuemin={0}
        aria-valuemax={100}
        className="h-2 w-full overflow-hidden rounded-full bg-muted"
      >
        <div className={cn("h-full rounded-full", tone.bar)} style={{ width: `${fillPercent}%` }} />
      </div>
      <p className={cn("text-sm", tone.text)}>
        {limit.level === "Exceeded"
          ? t("exceeded", {
              excess: formatMoney(Number(limit.excessKop), locale),
              tax: formatMoney(Number(limit.excessTaxKop), locale),
            })
          : limit.level === "Warn"
            ? t("remainingBeforeLimit", { amount: formatMoney(Number(limit.remainingKop), locale) })
            : t("remainingBeforeWarn", { amount: formatMoney(Number(limit.remainingKop), locale) })}
      </p>
    </section>
  );
}
