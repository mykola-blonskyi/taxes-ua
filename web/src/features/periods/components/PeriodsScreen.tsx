"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { LoadState } from "@/data/api/LoadState";
import { useTaxYears } from "@/data/tax-years/useTaxYears";
import { usePeriods } from "@/data/periods/usePeriods";
import { currentYearInKyiv } from "@/shared/lib/dates";
import { DeclarationNumbers } from "./DeclarationNumbers";
import { MonthsTable } from "./MonthsTable";
import { PeriodWarnings } from "./PeriodWarnings";
import { QuartersTable } from "./QuartersTable";

function defaultYear(configuredYears: number[]): number | undefined {
  if (configuredYears.length === 0) {
    return undefined;
  }

  const current = currentYearInKyiv();
  return configuredYears.includes(current) ? current : Math.max(...configuredYears);
}

export function PeriodsScreen() {
  const t = useTranslations("periods");
  const taxYearsQuery = useTaxYears();
  const { data: taxYears } = taxYearsQuery;
  const configuredYears = useMemo(
    () => (taxYears ?? []).map((taxYear) => Number(taxYear.year)).sort((a, b) => a - b),
    [taxYears],
  );
  const [selectedYear, setSelectedYear] = useState<number | undefined>(undefined);
  const year = selectedYear ?? defaultYear(configuredYears);

  const periodsQuery = usePeriods(year);
  const { data: periods, isLoading: periodsLoading, isError } = periodsQuery;

  if (taxYearsQuery.isLoading || taxYearsQuery.isError) {
    return <LoadState query={taxYearsQuery} loading={t("loading")} failed={t("loadFailed")} />;
  }

  if (configuredYears.length === 0) {
    return (
      <div className="flex flex-col gap-2">
        <p className="text-sm text-muted-foreground">{t("noYears")}</p>
        <Link href="/settings" className="text-sm font-medium text-primary underline-offset-4 hover:underline pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center">
          {t("noYearsCta")}
        </Link>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-1">
        <label htmlFor="periods-year" className="text-sm font-medium">
          {t("yearLabel")}
        </label>
        <select
          id="periods-year"
          value={year}
          onChange={(event) => setSelectedYear(Number(event.target.value))}
          className="w-full max-w-32 rounded-lg border bg-background px-2 py-1.5 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring pointer-coarse:min-h-11"
        >
          {configuredYears.map((configuredYear) => (
            <option key={configuredYear} value={configuredYear}>
              {configuredYear}
            </option>
          ))}
        </select>
      </div>

      {periodsLoading || isError ? (
        <LoadState query={periodsQuery} resetKey={year} loading={t("loading")} failed={t("loadFailed")} />
      ) : null}

      {!periodsLoading && !isError && periods ? (
        <PeriodWarnings year={Number(periods.year)} warnings={periods.warnings} limitCrossing={periods.limitCrossing} />
      ) : null}

      {!periodsLoading && !isError && periods && periods.quarters.length === 0 && periods.limitCrossing === null ? (
        <p className="text-sm text-muted-foreground">{t("notRegistered")}</p>
      ) : null}

      {!periodsLoading && !isError && periods && periods.quarters.length > 0 ? (
        <>
          <section className="flex flex-col gap-2">
            <h3 className="text-base font-semibold">{t("quartersTitle")}</h3>
            <QuartersTable quarters={periods.quarters} />
          </section>

          {periods.months ? (
            <section className="flex flex-col gap-2">
              <h3 className="text-base font-semibold">{t("monthsTitle")}</h3>
              <MonthsTable months={periods.months} />
            </section>
          ) : null}

          <section className="flex flex-col gap-2">
            <h3 className="text-base font-semibold">{t("declarationTitle")}</h3>
            <DeclarationNumbers year={Number(periods.year)} quarters={periods.quarters.filter((quarter) => quarter.group3)} />
          </section>
        </>
      ) : null}
    </div>
  );
}
