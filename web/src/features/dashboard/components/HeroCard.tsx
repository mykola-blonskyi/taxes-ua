"use client";

import { useRef, useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import type { KindDebt } from "@/data/dashboard/useDashboard";
import { useRecordPayments } from "@/data/payments/usePayments";
import { todayInKyiv } from "@/shared/lib/dates";
import { formatMoney } from "@/shared/lib/money";
import { cn } from "@/shared/lib/utils";
import { Button } from "@/shared/ui/button";
import { formatLongDate, periodOf, quarterEndOf } from "./debt";
import { DebtPeriod, DaysLeft } from "./DebtParts";

const electronicCabinetUrl = "https://cabinet.tax.gov.ua/";

// `now` is earliest first. Each debt keeps its own amount and there is no total: Rule 7 never adds
// kinds together.
export function HeroCard({ now, today, busy }: { now: KindDebt[]; today: string; busy: boolean }) {
  const t = useTranslations("dashboard");
  const tKinds = useTranslations("payments.kinds");
  const locale = useLocale();
  const { dueDate, status, daysLeft } = now[0];
  const overdue = status === "Overdue";
  const mixedDates = now.some((debt) => debt.dueDate !== dueDate);
  // ESV is a fixed monthly sum accrued up front; only the income-based kinds can still grow.
  const quarterOpen = now.some((debt) => debt.kind !== "Esv" && today <= quarterEndOf(debt));
  const kinds = new Intl.ListFormat(locale, { type: "conjunction" }).format(now.map((debt) => tKinds(debt.kind)));

  return (
    <section
      aria-labelledby="next-step-kinds"
      className={cn(
        "flex flex-col gap-4 rounded-xl border p-4 md:p-6",
        overdue ? "border-destructive/60 bg-destructive/5" : "bg-card",
      )}
    >
      <div className="flex flex-col gap-1">
        <p className={cn("text-sm", overdue ? "font-medium text-destructive" : "text-muted-foreground")}>
          {overdue ? t("overdueSince") : t("payBy")}
        </p>
        <p className={cn("text-4xl font-semibold tracking-tight md:text-5xl", overdue && "text-destructive")}>
          {formatLongDate(dueDate, today, locale)}
        </p>
        <DaysLeft days={Number(daysLeft)} className="block text-sm font-medium" />
      </div>

      <div className="flex flex-col gap-2">
        <h3 id="next-step-kinds" className="text-lg font-semibold">
          {t("pay", { kinds })}
        </h3>
        <ul className="flex flex-col gap-1">
          {now.map((debt) => (
            <li key={debt.kind} className="grid grid-cols-[minmax(0,1fr)_auto] items-baseline gap-x-4">
              <span className="text-sm text-muted-foreground">
                {tKinds(debt.kind)}, <DebtPeriod debt={debt} />
                {mixedDates ? ` · ${formatLongDate(debt.dueDate, today, locale)}` : null}
              </span>
              <span className="text-xl font-semibold tabular-nums">{formatMoney(Number(debt.amountKop), locale)}</span>
            </li>
          ))}
        </ul>
        {quarterOpen ? <p className="text-xs text-muted-foreground">{t("quarterOpenHint")}</p> : null}
      </div>

      <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-start">
        <MarkPaid now={now} busy={busy} />
        <a
          href={electronicCabinetUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="inline-flex h-8 items-center justify-center rounded-lg border border-border bg-background px-2.5 text-sm font-medium hover:bg-muted"
        >
          {t("openCabinet")}
        </a>
      </div>
    </section>
  );
}

function MarkPaid({ now, busy }: { now: KindDebt[]; busy: boolean }) {
  const t = useTranslations("dashboard");
  const recordPayments = useRecordPayments();
  const recording = useRef(false);
  const step = now.map((debt) => `${debt.kind}-${debt.fromYear}-${debt.fromQuarter}-${debt.advanceMonth}-${debt.amountKop}`).join();
  // Confirmation belongs to the step it was opened for, so a background refetch that changes the step
  // closes it rather than letting it record the new amounts.
  const [confirmingStep, setConfirmingStep] = useState<string | null>(null);
  const disabled = busy || recordPayments.isPending;

  function record() {
    if (recording.current) {
      return;
    }

    recording.current = true;
    const paidOn = todayInKyiv();
    recordPayments.mutate(
      now.map((debt) => {
        // Allocation settles the oldest debt first whatever period a payment names, so naming the
        // oldest open quarter, or the advance's month, only keeps the record readable.
        const { fromYear, fromQuarter } = periodOf(debt);
        const advance = debt.advanceMonth !== null;
        return {
          paidOn,
          kind: debt.kind,
          amountKop: debt.amountKop,
          periodYear: fromYear,
          periodQuarter: advance ? null : fromQuarter,
          periodMonth: advance ? debt.advanceMonth : null,
          note: null,
        };
      }),
      {
        onSettled: () => {
          recording.current = false;
          setConfirmingStep(null);
        },
      },
    );
  }

  return (
    <div className="flex flex-col gap-2">
      {confirmingStep === step ? (
        <div className="flex flex-col gap-2 rounded-lg border p-3">
          <p className="text-sm">{t("confirmMarkPaid", { count: now.length })}</p>
          <div className="flex flex-wrap gap-2">
            <Button onClick={record} disabled={disabled}>
              {t("confirm")}
            </Button>
            <Button variant="outline" onClick={() => setConfirmingStep(null)} disabled={recordPayments.isPending}>
              {t("cancel")}
            </Button>
          </div>
        </div>
      ) : (
        <Button onClick={() => setConfirmingStep(step)} disabled={disabled}>
          {t("markPaid")}
        </Button>
      )}
      {recordPayments.isError ? <p className="text-sm text-destructive">{t("markPaidFailed")}</p> : null}
    </div>
  );
}
