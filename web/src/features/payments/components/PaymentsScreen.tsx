"use client";

import { useMemo, useRef, useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { LoadState } from "@/data/api/LoadState";
import { usePayments, type PaymentResponse } from "@/data/payments/usePayments";
import { usePeriods } from "@/data/periods/usePeriods";
import { useTaxYears } from "@/data/tax-years/useTaxYears";
import { currentYearInKyiv } from "@/shared/lib/dates";
import { BalancesPanel } from "./BalancesPanel";
import { ObligationsPanel } from "./ObligationsPanel";
import { PaymentForm } from "./PaymentForm";
import { PaymentList } from "./PaymentList";

function defaultYear(configuredYears: number[]): number | undefined {
  if (configuredYears.length === 0) {
    return undefined;
  }

  const current = currentYearInKyiv();
  return configuredYears.includes(current) ? current : Math.max(...configuredYears);
}

export function PaymentsScreen() {
  const t = useTranslations("payments");
  const taxYearsQuery = useTaxYears();
  const { data: taxYears } = taxYearsQuery;
  const configuredYears = useMemo(
    () => (taxYears ?? []).map((taxYear) => Number(taxYear.year)).sort((a, b) => a - b),
    [taxYears],
  );
  const [selectedYear, setSelectedYear] = useState<number | undefined>(undefined);
  const year = selectedYear ?? defaultYear(configuredYears);
  const [editing, setEditing] = useState<PaymentResponse | null>(null);
  const formRef = useRef<HTMLDivElement>(null);

  const periods = usePeriods(year);
  const payments = usePayments(year);

  if (taxYearsQuery.isLoading || taxYearsQuery.isError) {
    return <LoadState query={taxYearsQuery} loading={t("loading")} failed={t("loadFailed")} />;
  }

  if (year === undefined) {
    return (
      <div className="flex flex-col gap-2">
        <p className="text-sm text-muted-foreground">{t("noYears")}</p>
        <Link href="/settings" className="text-sm font-medium text-primary underline-offset-4 hover:underline pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center">
          {t("noYearsCta")}
        </Link>
      </div>
    );
  }

  function startEdit(payment: PaymentResponse) {
    setEditing(payment);
    formRef.current?.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-1">
        <label htmlFor="payments-year" className="text-sm font-medium">
          {t("yearLabel")}
        </label>
        <select
          id="payments-year"
          value={year}
          onChange={(event) => {
            setSelectedYear(Number(event.target.value));
            setEditing(null);
          }}
          className="w-full max-w-32 rounded-lg border bg-background px-2 py-1.5 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring pointer-coarse:min-h-11"
        >
          {configuredYears.map((configuredYear) => (
            <option key={configuredYear} value={configuredYear}>
              {configuredYear}
            </option>
          ))}
        </select>
      </div>

      {payments.isLoading || periods.isLoading || payments.isError || periods.isError ? (
        <LoadState query={[payments, periods]} resetKey={year} loading={t("loading")} failed={t("loadFailed")} />
      ) : null}

      {periods.data?.balances ? <BalancesPanel year={year} balances={periods.data.balances} /> : null}

      {periods.data ? <ObligationsPanel year={year} quarters={periods.data.quarters} /> : null}

      {periods.data?.warnings.fopRegistrationDateNotSet ? (
        <LedgerNotice title={t("registrationWarning.title")} message={t("registrationWarning.message")} cta={t("registrationWarning.cta")} />
      ) : null}

      {periods.data?.warnings.yearBeforeRegistration ? (
        <LedgerNotice message={t("ledgerWarning.yearBeforeRegistration", { year })} />
      ) : null}

      {periods.data && periods.data.warnings.missingTaxYear !== null ? (
        <LedgerNotice
          message={t("ledgerWarning.missingTaxYear", { year: Number(periods.data.warnings.missingTaxYear) })}
          cta={t("registrationWarning.cta")}
        />
      ) : null}

      <div ref={formRef}>
        <PaymentForm
          key={editing?.id ?? `new-${year}`}
          year={year}
          configuredYears={configuredYears}
          editing={editing}
          onDone={() => setEditing(null)}
        />
      </div>

      {payments.data ? (
        payments.data.items.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("empty")}</p>
        ) : (
          <PaymentList items={payments.data.items} onEdit={startEdit} />
        )
      ) : null}
    </div>
  );
}

function LedgerNotice({ title, message, cta }: { title?: string; message: string; cta?: string }) {
  return (
    <section className="flex flex-col gap-2 rounded-lg border bg-muted p-4">
      {title ? <h3 className="text-sm font-semibold text-destructive">{title}</h3> : null}
      <p className="text-sm text-muted-foreground">{message}</p>
      {cta ? (
        <Link href="/settings" className="text-sm font-medium text-primary underline-offset-4 hover:underline pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center">
          {cta}
        </Link>
      ) : null}
    </section>
  );
}
