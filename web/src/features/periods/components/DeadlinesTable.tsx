"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { useTaxYears } from "@/data/tax-years/useTaxYears";
import { usePeriods, type PeriodsResponse } from "@/data/periods/usePeriods";

type Quarter = PeriodsResponse["quarters"][number];
type Deadline = Quarter["deadlines"]["esv"];

function currentYearInKyiv(): number {
  return Number(
    new Intl.DateTimeFormat("en-US", { year: "numeric", timeZone: "Europe/Kyiv" }).format(new Date()),
  );
}

function defaultYear(configuredYears: number[]): number | undefined {
  if (configuredYears.length === 0) {
    return undefined;
  }

  const current = currentYearInKyiv();
  return configuredYears.includes(current) ? current : Math.max(...configuredYears);
}

function formatDate(value: string, locale: string): string {
  return new Intl.DateTimeFormat(locale, { day: "2-digit", month: "2-digit", year: "numeric", timeZone: "UTC" }).format(
    new Date(`${value}T00:00:00Z`),
  );
}

export function DeadlinesTable() {
  const t = useTranslations("periods");
  const locale = useLocale();
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
      {!periodsLoading && !isError && periods && periods.quarters.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t("notRegistered")}</p>
      ) : null}

      {!periodsLoading && !isError && periods && periods.quarters.length > 0 ? (
        <table className="w-full border-collapse text-sm">
          <thead>
            <tr className="border-b text-left align-bottom text-xs text-muted-foreground">
              <th className="p-2 font-medium">{t("quarter")}</th>
              <th className="p-2 font-medium">{t("esv")}</th>
              <th className="p-2 font-medium">{t("declaration")}</th>
              <th className="p-2 font-medium">{t("taxPayment")}</th>
            </tr>
          </thead>
          <tbody>
            {periods.quarters.map((quarter) => (
              <QuarterRow key={quarter.quarter} quarter={quarter} locale={locale} />
            ))}
          </tbody>
        </table>
      ) : null}
    </div>
  );
}

function QuarterRow({ quarter, locale }: { quarter: Quarter; locale: string }) {
  const t = useTranslations("periods");

  return (
    <tr className="border-b align-top">
      <td className="p-2">{t("quarterNumber", { quarter: quarter.quarter })}</td>
      <DeadlineCell deadline={quarter.deadlines.esv} locale={locale} />
      <DeadlineCell deadline={quarter.deadlines.declaration} locale={locale} />
      <DeadlineCell deadline={quarter.deadlines.taxPayment} locale={locale} />
    </tr>
  );
}

function DeadlineCell({ deadline, locale }: { deadline: Deadline; locale: string }) {
  const t = useTranslations("periods");
  const shifted = deadline.statutory !== deadline.due;

  return (
    <td className="p-2">
      <div>{formatDate(deadline.due, locale)}</div>
      {shifted ? (
        <div className="text-xs text-muted-foreground">
          {t("shiftedFrom", { date: formatDate(deadline.statutory, locale) })}
        </div>
      ) : null}
    </td>
  );
}
