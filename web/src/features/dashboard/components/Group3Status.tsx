"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import type { DashboardResponse } from "@/data/dashboard/useDashboard";
import { formatMoney } from "@/shared/lib/money";
import { formatLongDate } from "./debt";
import { DaysLeft } from "./DebtParts";

type Group3 = DashboardResponse["group3"];

export function Group3Status({ group3, today }: { group3: Group3; today: string }) {
  const unconfirmed = !group3.confirmed && group3.group3Start !== null;

  return (
    <>
      {unconfirmed ? <UnconfirmedBanner group3={group3} today={today} /> : null}
      {group3.beforeGroup3 ? <BeforeGroup3 stretch={group3.beforeGroup3} today={today} /> : null}
    </>
  );
}

function UnconfirmedBanner({ group3, today }: { group3: Group3; today: string }) {
  const t = useTranslations("dashboard.group3");
  const locale = useLocale();

  return (
    <section
      role="status"
      className="flex min-w-0 flex-col gap-2 rounded-lg border border-amber-500/50 bg-amber-500/10 p-4 text-sm"
    >
      <p className="break-words font-medium text-amber-800 dark:text-amber-300">{t("unconfirmed")}</p>
      {group3.applicationDeadline ? (
        <p className="break-words">
          {t("applyBy", { date: formatLongDate(group3.applicationDeadline, today, locale) })}
          {" · "}
          <DaysLeft days={Number(group3.applicationDaysLeft)} />
        </p>
      ) : null}
      <Link href="/settings?tab=dps" className="w-fit font-medium text-primary underline-offset-4 hover:underline">
        {t("checkStatus")}
      </Link>
    </section>
  );
}

function BeforeGroup3({ stretch, today }: { stretch: NonNullable<Group3["beforeGroup3"]>; today: string }) {
  const t = useTranslations("dashboard.group3");
  const tPeriods = useTranslations("periods.warnings");
  const locale = useLocale();

  return (
    <section
      role="note"
      className="flex min-w-0 flex-col gap-2 rounded-lg border border-amber-500/50 bg-amber-500/10 p-4 text-sm"
    >
      <h3 className="font-semibold text-amber-800 dark:text-amber-300">{t("beforeGroup3Title")}</h3>
      <p className="break-words">
        {tPeriods("beforeGroup3", {
          from: formatLongDate(stretch.from, today, locale),
          to: formatLongDate(stretch.to, today, locale),
          income: formatMoney(Number(stretch.incomeKop), locale),
        })}
      </p>
    </section>
  );
}
