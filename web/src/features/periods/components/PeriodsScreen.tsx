"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
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
  const { data: taxYears, isLoading: taxYearsLoading, isError: taxYearsFailed } = useTaxYears();
  const configuredYears = useMemo(
    () => (taxYears ?? []).map((taxYear) => Number(taxYear.year)).sort((a, b) => a - b),
    [taxYears],
  );
  const [selectedYear, setSelectedYear] = useState<number | undefined>(undefined);
  const year = selectedYear ?? defaultYear(configuredYears);

  const { data: periods, isLoading: periodsLoading, isError } = usePeriods(year);

  if (taxYearsLoading) {
    return <p className="text-sm text-muted-foreground">{t("loading")}</p>;
  }

  if (taxYearsFailed) {
    return <p className="text-sm text-destructive">{t("loadFailed")}</p>;
  }

  if (configuredYears.length === 0) {
    return (
      <div className="flex flex-col gap-2">
        <p className="text-sm text-muted-foreground">{t("noYears")}</p>
        <Link href="/settings" className="text-sm font-medium text-primary underline-offset-4 hover:underline">
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
          className="w-full max-w-32 rounded-lg border bg-background px-2 py-1.5 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50"
        >
          {configuredYears.map((configuredYear) => (
            <option key={configuredYear} value={configuredYear}>
              {configuredYear}
            </option>
          ))}
        </select>
      </div>

      {periodsLoading ? <p className="text-sm text-muted-foreground">{t("loading")}</p> : null}
      {isError ? <p className="text-sm text-destructive">{t("loadFailed")}</p> : null}

      {!periodsLoading && !isError && periods ? (
        <PeriodWarnings year={Number(periods.year)} warnings={periods.warnings} limitCrossing={periods.limitCrossing} />
      ) : null}

      {!periodsLoading && !isError && periods && periods.quarters.length === 0 ? (
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
            <DeclarationNumbers year={Number(periods.year)} quarters={periods.quarters} />
          </section>
        </>
      ) : null}
    </div>
  );
}
