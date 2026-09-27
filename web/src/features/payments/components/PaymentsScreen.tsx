"use client";

import { useMemo, useRef, useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { usePayments, type PaymentResponse } from "@/data/payments/usePayments";
import { usePeriods } from "@/data/periods/usePeriods";
import { useTaxYears } from "@/data/tax-years/useTaxYears";
import { currentYearInKyiv } from "@/shared/lib/dates";
import { BalancesPanel } from "./BalancesPanel";
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
  const { data: taxYears, isLoading: taxYearsLoading, isError: taxYearsFailed } = useTaxYears();
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

  if (taxYearsLoading) {
    return <p className="text-sm text-muted-foreground">{t("loading")}</p>;
  }

  if (taxYearsFailed) {
    return <p className="text-sm text-destructive">{t("loadFailed")}</p>;
  }

  if (year === undefined) {
    return (
      <div className="flex flex-col gap-2">
        <p className="text-sm text-muted-foreground">{t("noYears")}</p>
        <Link href="/settings" className="text-sm font-medium text-primary underline-offset-4 hover:underline">
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
          className="w-full max-w-32 rounded-lg border bg-background px-2 py-1.5 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50"
        >
          {configuredYears.map((configuredYear) => (
            <option key={configuredYear} value={configuredYear}>
              {configuredYear}
            </option>
          ))}
        </select>
      </div>

      {payments.isLoading || periods.isLoading ? (
        <p className="text-sm text-muted-foreground">{t("loading")}</p>
      ) : null}
      {payments.isError || periods.isError ? <p className="text-sm text-destructive">{t("loadFailed")}</p> : null}

      {periods.data?.balances ? <BalancesPanel year={year} balances={periods.data.balances} /> : null}

      {periods.data && periods.data.balances === null ? (
        <section className="flex flex-col gap-2 rounded-lg border bg-muted p-4">
          <h3 className="text-sm font-semibold text-destructive">{t("registrationWarning.title")}</h3>
          <p className="text-sm text-muted-foreground">{t("registrationWarning.message")}</p>
          <Link href="/settings" className="text-sm font-medium text-primary underline-offset-4 hover:underline">
            {t("registrationWarning.cta")}
          </Link>
        </section>
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
