"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import type { DashboardResponse } from "@/data/dashboard/useDashboard";
import { formatInstantInKyiv } from "@/shared/lib/dates";

type Sync = NonNullable<DashboardResponse["sync"]>;

const problems = {
  Stale: { key: "stale", tone: "border-amber-500/50 bg-amber-500/10 text-amber-800 dark:text-amber-300" },
  TokenRejected: { key: "tokenRejected", tone: "border-destructive/40 bg-destructive/10 text-destructive" },
  TokenUnreadable: { key: "tokenUnreadable", tone: "border-destructive/40 bg-destructive/10 text-destructive" },
} as const;

export function SyncHealth({ sync }: { sync: Sync }) {
  const t = useTranslations("dashboard.sync");
  const locale = useLocale();
  const when = sync.lastSyncedAt ? formatInstantInKyiv(sync.lastSyncedAt, locale) : null;

  if (sync.state === "Healthy") {
    return (
      <p className="text-xs text-muted-foreground">{when ? t("healthy", { when }) : t("backfilling")}</p>
    );
  }

  const problem = problems[sync.state];

  return (
    <section role="alert" className={`flex min-w-0 flex-col gap-2 rounded-lg border p-4 ${problem.tone}`}>
      <h3 className="break-words text-sm font-semibold">{t(`${problem.key}.title`)}</h3>
      <p className="break-words text-sm">{t(`${problem.key}.text`, { when: when ?? "" })}</p>
      <Link
        href="/settings?tab=monobank"
        className="w-fit text-sm font-medium text-primary underline-offset-4 hover:underline"
      >
        {t("fix")}
      </Link>
    </section>
  );
}
