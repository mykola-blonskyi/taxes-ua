"use client";

import type { ReactNode } from "react";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { TriangleAlert } from "lucide-react";
import type { PeriodsResponse } from "@/data/periods/usePeriods";
import { formatDateOnly } from "@/shared/lib/dates";
import { formatMoney } from "@/shared/lib/money";
import { cn } from "@/shared/lib/utils";

type Warnings = PeriodsResponse["warnings"];

export function PeriodWarnings({
  year,
  warnings,
  limitCrossing,
}: {
  year: number;
  warnings: Warnings;
  limitCrossing: PeriodsResponse["limitCrossing"];
}) {
  const t = useTranslations("periods");
  const locale = useLocale();

  const excludedOperationCount = Number(warnings.excludedOperationCount);
  const negativeQuarters = warnings.negativeCumulativeTaxQuarters.map((quarter) => Number(quarter));

  const missingTaxYear = warnings.missingTaxYear === null ? null : Number(warnings.missingTaxYear);

  const hasWarning =
    warnings.taxYearUnverified ||
    warnings.fopRegistrationDateNotSet ||
    warnings.yearBeforeRegistration ||
    missingTaxYear !== null ||
    excludedOperationCount > 0 ||
    negativeQuarters.length > 0 ||
    limitCrossing !== null ||
    Boolean(warnings.beforeGroup3);

  if (!hasWarning) {
    return null;
  }

  return (
    <ul className="flex flex-col gap-1.5">
      {limitCrossing !== null ? <LimitCrossingItem year={year} crossing={limitCrossing} /> : null}

      {warnings.beforeGroup3 ? (
        <WarningItem tone="warning">
          {t("warnings.beforeGroup3", {
            from: formatDateOnly(warnings.beforeGroup3.from, locale),
            to: formatDateOnly(warnings.beforeGroup3.to, locale),
            income: formatMoney(Number(warnings.beforeGroup3.incomeKop), locale),
          })}{" "}
          <Link href="/settings?tab=dps" className="font-medium text-primary underline-offset-4 hover:underline">
            {t("warnings.dpsStatusCta")}
          </Link>
        </WarningItem>
      ) : null}

      {warnings.taxYearUnverified ? (
        <WarningItem tone="destructive">
          {t("warnings.taxYearUnverified", { year })}{" "}
          <Link href="/settings" className="font-medium text-primary underline-offset-4 hover:underline">
            {t("noYearsCta")}
          </Link>
        </WarningItem>
      ) : null}

      {warnings.fopRegistrationDateNotSet ? (
        <WarningItem tone="destructive">
          {t("warnings.fopRegistrationDateNotSet")}{" "}
          <Link href="/settings" className="font-medium text-primary underline-offset-4 hover:underline">
            {t("noYearsCta")}
          </Link>
        </WarningItem>
      ) : null}

      {warnings.yearBeforeRegistration ? (
        <WarningItem tone="muted">{t("warnings.yearBeforeRegistration", { year })}</WarningItem>
      ) : null}

      {missingTaxYear !== null ? (
        <WarningItem tone="destructive">
          {t("warnings.missingTaxYear", { year: missingTaxYear })}{" "}
          <Link href="/settings" className="font-medium text-primary underline-offset-4 hover:underline">
            {t("noYearsCta")}
          </Link>
        </WarningItem>
      ) : null}

      {excludedOperationCount > 0 ? (
        <WarningItem tone="muted">
          {t("warnings.excludedOperations", { count: excludedOperationCount })}{" "}
          <Link href="/transactions" className="font-medium text-primary underline-offset-4 hover:underline">
            {t("warnings.goToTransactions")}
          </Link>
        </WarningItem>
      ) : null}

      {negativeQuarters.length > 0 ? (
        <WarningItem tone="destructive">
          {t("warnings.negativeCumulativeTax", {
            quarters: negativeQuarters.map((quarter) => t("quarterNumber", { quarter })).join(", "),
          })}
        </WarningItem>
      ) : null}
    </ul>
  );
}

function LimitCrossingItem({
  year,
  crossing,
}: {
  year: number;
  crossing: NonNullable<PeriodsResponse["limitCrossing"]>;
}) {
  const t = useTranslations("periods");
  const switchFrom = { year: Number(crossing.switchFromYear), quarter: Number(crossing.switchFromQuarter) };
  const back = crossing.backOnGroup3From
    ? { year: Number(crossing.backOnGroup3From.year), quarter: Number(crossing.backOnGroup3From.quarter) }
    : null;
  const notComputed = [1, 2, 3, 4].filter(
    (quarter) => !isBefore({ year, quarter }, switchFrom) && (back === null || isBefore({ year, quarter }, back)),
  );
  const switchValues = { switchQuarter: switchFrom.quarter, switchYear: switchFrom.year };

  return (
    <WarningItem tone="destructive">
      <span className="font-semibold">
        {t("warnings.limitCrossing", { quarter: Number(crossing.quarter), year: Number(crossing.year) })}
      </span>{" "}
      {notComputed.length === 0
        ? t("warnings.limitCrossingNextYear", switchValues)
        : t("warnings.limitCrossingNotComputed", {
            quarters: notComputed.map((quarter) => t("quarterNumber", { quarter })).join(", "),
            ...switchValues,
          })}
      {back !== null ? <> {t("warnings.backOnGroup3", back)}</> : null}
    </WarningItem>
  );
}

function isBefore(left: { year: number; quarter: number }, right: { year: number; quarter: number }): boolean {
  return left.year < right.year || (left.year === right.year && left.quarter < right.quarter);
}

const toneClasses = {
  destructive: "text-destructive",
  warning: "text-amber-700 dark:text-amber-400",
  muted: "text-muted-foreground",
} as const;

function WarningItem({ tone, children }: { tone: keyof typeof toneClasses; children: ReactNode }) {
  return (
    <li
      className={cn("flex items-start gap-1.5 text-sm", toneClasses[tone])}
    >
      <TriangleAlert className="mt-0.5 size-3.5 shrink-0" aria-hidden="true" />
      <span className="min-w-0 break-words">{children}</span>
    </li>
  );
}
