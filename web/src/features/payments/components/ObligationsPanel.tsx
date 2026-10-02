"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { paymentKinds, type PaymentKind } from "@/data/payments/usePayments";
import { isPeriodNotComputed, usePaymentDetails } from "@/data/payments/usePaymentDetails";
import type { PeriodsResponse } from "@/data/periods/usePeriods";
import { formatNumericDate } from "@/shared/lib/dates";
import { formatMoney } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";
import { PayPanel } from "@/shared/ui/pay-panel";

type Quarter = PeriodsResponse["quarters"][number];

const kindField = {
  SingleTax: "singleTax",
  MilitaryLevy: "militaryLevy",
  Esv: "esv",
} as const;

// The api lists only quarters in group 3 and gives obligations only inside the ledger (Rule 7), so
// every quarter offered here is one the payment-details endpoint accepts.
export function ObligationsPanel({ year, quarters }: { year: number; quarters: Quarter[] }) {
  const t = useTranslations("pay.obligations");
  const tKinds = useTranslations("payments.kinds");
  const tDashboard = useTranslations("dashboard");
  const locale = useLocale();
  const offered = quarters.filter((quarter) => quarter.obligations !== null);

  if (offered.length === 0) {
    return null;
  }

  return (
    <section className="flex flex-col gap-2">
      <h3 className="text-base font-semibold">{t("title", { year })}</h3>
      <p className="text-xs text-muted-foreground">{t("hint")}</p>
      <div className="grid gap-2 md:grid-cols-2">
        {offered.map((quarter) => {
          const quarterNumber = Number(quarter.quarter);
          const quarterLabel = tDashboard("quarter", { quarter: quarterNumber, year });

          return (
            <div key={quarterNumber} className="flex min-w-0 flex-col gap-1 rounded-lg border p-3">
              <h4 className="text-sm font-semibold">{quarterLabel}</h4>
              <ul className="flex flex-col divide-y">
                {paymentKinds.map((kind) => {
                  const obligation = quarter.obligations![kindField[kind]];

                  return (
                    <li key={kind} className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 py-2">
                      <div className="flex min-w-0 flex-col">
                        <span className="text-sm font-medium">{tKinds(kind)}</span>
                        <span className="text-xs text-muted-foreground">
                          {t("remaining", { amount: formatMoney(Number(obligation.remainingKop), locale) })}
                          {" · "}
                          {t("dueBy", { date: formatNumericDate(obligation.dueDate, locale) })}
                        </span>
                      </div>
                      <PayObligationButton
                        kind={kind}
                        year={year}
                        quarter={quarterNumber}
                        title={`${tKinds(kind)}, ${quarterLabel}`}
                        remainingKop={Number(obligation.remainingKop)}
                      />
                    </li>
                  );
                })}
              </ul>
            </div>
          );
        })}
      </div>
    </section>
  );
}

function PayObligationButton({
  kind,
  year,
  quarter,
  title,
  remainingKop,
}: {
  kind: PaymentKind;
  year: number;
  quarter: number;
  title: string;
  remainingKop: number;
}) {
  const t = useTranslations("pay");
  const [open, setOpen] = useState(false);
  const startKop = remainingKop > 0 ? remainingKop : null;
  const [amountKop, setAmountKop] = useState<number | null>(startKop);
  const details = usePaymentDetails(
    { kind, periodYear: year, periodQuarter: quarter, periodMonth: null },
    amountKop,
    open,
  );
  const notComputed = isPeriodNotComputed(details.error);

  return (
    <>
      <Button
        type="button"
        variant="outline"
        size="sm"
        onClick={() => {
          setAmountKop(startKop);
          setOpen(true);
        }}
      >
        {t("button")}
      </Button>
      <PayPanel
        open={open}
        onOpenChange={setOpen}
        title={title}
        initialAmountKop={startKop}
        onAmountChange={setAmountKop}
        details={details.data}
        loading={details.isFetching}
        failed={details.isError && !notComputed}
        onRetry={() => details.refetch()}
        notComputed={notComputed}
      />
    </>
  );
}
