"use client";

import { useMemo, useState } from "react";
import { Download } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { LoadState } from "@/data/api/LoadState";
import { useArchive, type ArchiveResponse } from "@/data/archive/useArchive";
import { useTaxYears } from "@/data/tax-years/useTaxYears";
import { currentYearInKyiv, formatDateOnly, formatInstantInKyiv } from "@/shared/lib/dates";
import { formatAmount } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";

type Quarter = ArchiveResponse["quarters"][number];
type Item = { id: string; name: string; url: string };

function parseYear(value: string | undefined): number | undefined {
  return value !== undefined && /^\d{4}$/.test(value) ? Number(value) : undefined;
}

export function ArchiveScreen({ year: yearParam }: { year?: string }) {
  const t = useTranslations("archive");
  const taxYearsQuery = useTaxYears();
  const [selected, setSelected] = useState<number | undefined>(undefined);
  const configuredYears = useMemo(
    () => (taxYearsQuery.data ?? []).map((taxYear) => Number(taxYear.year)),
    [taxYearsQuery.data],
  );
  const current = currentYearInKyiv();
  const year =
    selected ??
    parseYear(yearParam) ??
    (configuredYears.length === 0 || configuredYears.includes(current) ? current : Math.max(...configuredYears));
  const years = useMemo(() => [...new Set([...configuredYears, year])].sort((a, b) => a - b), [configuredYears, year]);
  const archiveQuery = useArchive(taxYearsQuery.isSuccess ? year : undefined);

  if (taxYearsQuery.isLoading || taxYearsQuery.isError) {
    return <LoadState query={taxYearsQuery} loading={t("loading")} failed={t("loadFailed")} />;
  }

  return (
    <div className="flex min-w-0 flex-col gap-4">
      {/* The year's keep-until date goes in this block, beside the year picker. */}
      <div className="flex flex-col gap-1">
        <label htmlFor="archive-year" className="text-sm font-medium">
          {t("yearLabel")}
        </label>
        <select
          id="archive-year"
          value={year}
          onChange={(event) => setSelected(Number(event.target.value))}
          className="w-full max-w-32 rounded-lg border bg-background px-2 py-1.5 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring pointer-coarse:min-h-11"
        >
          {years.map((option) => (
            <option key={option} value={option}>
              {option}
            </option>
          ))}
        </select>
      </div>

      {archiveQuery.data ? (
        <Documents archive={archiveQuery.data} />
      ) : (
        <LoadState query={archiveQuery} resetKey={year} loading={t("loading")} failed={t("loadFailed")} />
      )}
    </div>
  );
}

function Documents({ archive }: { archive: ArchiveResponse }) {
  const t = useTranslations("archive");
  const locale = useLocale();
  const tStatus = useTranslations("invoices.status");

  return (
    <div className="flex min-w-0 flex-col gap-6">
      <Section title={t("invoices.title")} empty={t("invoices.empty")} count={archive.invoices.length}>
        {archive.invoices.map((invoice) => (
          <Row
            key={invoice.id}
            item={invoice}
            title={`${invoice.number} · ${invoice.client}`}
            details={[
              tStatus(invoice.status),
              formatDateOnly(invoice.issueDate, locale),
              formatAmount(Number(invoice.totalMinor), invoice.currency, locale),
            ]}
          />
        ))}
      </Section>

      <Section title={t("declarations.title")} empty={t("declarations.empty")} count={archive.quarters.length}>
        {archive.quarters.map((quarter) => (
          <QuarterRow key={quarter.quarter} quarter={quarter} year={Number(archive.year)} />
        ))}
      </Section>

      <Section title={t("statements.title")} empty={t("statements.empty")} count={archive.statements.length}>
        {archive.statements.map((item) => (
          <Row key={item.id} item={item} title={item.name} details={[t("statements.hint")]} />
        ))}
      </Section>

      <Section title={t("payments.title")} empty={t("payments.empty")} count={archive.payments.length}>
        {archive.payments.map((item) => (
          <Row key={item.id} item={item} title={item.name} details={[t("payments.hint")]} />
        ))}
      </Section>
    </div>
  );
}

function Section({
  title,
  empty,
  count,
  children,
}: {
  title: string;
  empty: string;
  count: number;
  children: React.ReactNode;
}) {
  return (
    <section className="flex min-w-0 flex-col gap-2" aria-label={title}>
      <h3 className="text-base font-semibold">{title}</h3>
      {count === 0 ? <p className="text-sm text-muted-foreground">{empty}</p> : <ul className="flex min-w-0 flex-col gap-2">{children}</ul>}
    </section>
  );
}

function QuarterRow({ quarter, year }: { quarter: Quarter; year: number }) {
  const t = useTranslations("archive.declarations");
  const tTypes = useTranslations("declaration.types");
  const locale = useLocale();

  return (
    <li className="flex min-w-0 flex-col gap-2 rounded-lg border bg-card p-3 text-sm">
      <div className="flex min-w-0 flex-col gap-0.5">
        <h4 className="font-medium">{t("period", { quarter: quarter.quarter, year })}</h4>
        <p className="text-xs text-muted-foreground">
          {quarter.filed
            ? t("filed", { date: formatDateOnly(quarter.filed.filedOn, locale), type: tTypes(quarter.filed.type) })
            : t("notFiled")}
        </p>
      </div>
      {quarter.files.length === 0 ? <p className="text-xs text-muted-foreground">{t("noFiles")}</p> : null}
      <ul className="flex min-w-0 flex-col gap-2">
        {quarter.files.map((file) => (
          <Row
            key={file.id}
            item={file}
            title={`${file.annex ? t("annex") : t("declaration")} · ${tTypes(file.type)}`}
            details={[file.name, formatInstantInKyiv(file.generatedAt, locale)]}
            nested
          />
        ))}
      </ul>
    </li>
  );
}

function Row({ item, title, details, nested = false }: { item: Item; title: string; details: string[]; nested?: boolean }) {
  const t = useTranslations("archive");

  return (
    <li
      className={
        nested
          ? "flex min-w-0 flex-col gap-1 border-t pt-2 text-sm"
          : "flex min-w-0 flex-col gap-1 rounded-lg border bg-card p-3 text-sm"
      }
    >
      <span className="break-words font-medium">{title}</span>
      {details.map((detail) => (
        <span key={detail} className="break-all text-xs text-muted-foreground">
          {detail}
        </span>
      ))}
      <Button asChild variant="outline" size="sm" className="w-fit">
        <a href={item.url} download={item.name} aria-label={t("download", { name: item.name })}>
          <Download aria-hidden="true" />
          {t("downloadShort")}
        </a>
      </Button>
    </li>
  );
}
