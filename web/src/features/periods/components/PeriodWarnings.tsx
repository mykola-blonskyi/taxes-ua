"use client";

import type { ReactNode } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { TriangleAlert } from "lucide-react";
import type { PeriodsResponse } from "@/data/periods/usePeriods";
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
    limitCrossing !== null;

  if (!hasWarning) {
    return null;
  }

  const switchQuarter = limitCrossing === null ? 0 : Number(limitCrossing.switchFromQuarter);
  const switchYear = limitCrossing === null ? 0 : Number(limitCrossing.switchFromYear);
  const skipped = Array.from({ length: Math.max(0, 5 - switchQuarter) }, (_, index) => switchQuarter + index)
    .map((quarter) => t("quarterNumber", { quarter }))
    .join(", ");

  return (
    <ul className="flex flex-col gap-1.5">
      {limitCrossing !== null ? (
        <WarningItem tone="destructive">
          <span className="font-semibold">{t("warnings.limitCrossing", { quarter: Number(limitCrossing.quarter) })}</span>{" "}
          {switchYear > year
            ? t("warnings.limitCrossingNextYear", { switchQuarter, switchYear })
            : t("warnings.limitCrossingSameYear", { quarters: skipped, switchQuarter, switchYear })}
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

function WarningItem({ tone, children }: { tone: "destructive" | "muted"; children: ReactNode }) {
  return (
    <li
      className={cn(
        "flex items-start gap-1.5 text-sm",
        tone === "destructive" ? "text-destructive" : "text-muted-foreground",
      )}
    >
      <TriangleAlert className="mt-0.5 size-3.5 shrink-0" aria-hidden="true" />
      <span className="min-w-0 break-words">{children}</span>
    </li>
  );
}
