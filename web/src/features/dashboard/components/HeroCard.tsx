"use client";

import { useRef, useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { recordedPeriodOf, type KindDebt } from "@/data/dashboard/useDashboard";
import { useRecordPayments, type PaymentKind } from "@/data/payments/usePayments";
import { formatMoney, formatPlainAmount, parseHryvnia } from "@/shared/lib/money";
import { cn } from "@/shared/lib/utils";
import { Button } from "@/shared/ui/button";
import { TextField } from "@/shared/ui/fields";
import { formatLongDate, quarterEndOf } from "./debt";
import { DebtPeriod, DaysLeft } from "./DebtParts";
import { PayDebtButton } from "./PayDebtButton";

const electronicCabinetUrl = "https://cabinet.tax.gov.ua/";

// The api's lowest payment year (TransactionsEndpoints.MinYear); a date before it is refused.
const earliestPaidOn = "2000-01-01";

// Names a debt for as long as its amount stands, so an amount edited for one figure is never carried to
// the next one a refetch brings.
function debtKey(debt: KindDebt) {
  return `${debt.kind}-${debt.fromYear}-${debt.fromQuarter}-${debt.advanceMonth}-${debt.amountKop}`;
}

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
  // What the owner typed into each debt's pay panel, by debt key; the confirm step starts from it.
  const [edited, setEdited] = useState<Record<string, number | null>>({});
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
              <PayDebtButton
                debt={debt}
                className="col-span-full mt-1 w-fit"
                onAmountChange={(amountKop) => setEdited((current) => ({ ...current, [debtKey(debt)]: amountKop }))}
              />
            </li>
          ))}
        </ul>
        {quarterOpen ? <p className="text-xs text-muted-foreground">{t("quarterOpenHint")}</p> : null}
      </div>

      <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-start">
        <MarkPaid now={now} today={today} edited={edited} busy={busy} />
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

// `debts` is what the form records. After a partial failure it is only the debts that were not saved, and
// `retry` keeps the form open when a refetch has already moved the step on.
type Draft = { step: string; debts: KindDebt[]; paidOn: string; amounts: Record<string, string>; retry: boolean };

function MarkPaid({
  now,
  today,
  edited,
  busy,
}: {
  now: KindDebt[];
  today: string;
  edited: Record<string, number | null>;
  busy: boolean;
}) {
  const t = useTranslations("dashboard");
  const tKinds = useTranslations("payments.kinds");
  const locale = useLocale();
  const recordPayments = useRecordPayments();
  const [outcome, setOutcome] = useState<{ saved: PaymentKind[]; failed: PaymentKind[] } | null>(null);
  const recording = useRef(false);
  const step = now.map(debtKey).join();
  // The draft belongs to the step it was opened for, so a background refetch that changes the step
  // closes it rather than letting it record the new amounts.
  const [draft, setDraft] = useState<Draft | null>(null);
  const disabled = busy || recordPayments.isPending;
  const open = draft && (draft.retry || draft.step === step) ? draft : null;
  const debts = open?.debts ?? now;

  const kindList = (kinds: PaymentKind[]) =>
    new Intl.ListFormat(locale, { type: "conjunction" }).format(kinds.map((kind) => tKinds(kind)));

  function openDraft() {
    setOutcome(null);
    setDraft({
      step,
      debts: now,
      retry: false,
      paidOn: today,
      amounts: Object.fromEntries(
        now.map((debt) => [debtKey(debt), formatPlainAmount(edited[debtKey(debt)] ?? Number(debt.amountKop))]),
      ),
    });
  }

  const amountsKop = open ? debts.map((debt) => parseHryvnia(open.amounts[debtKey(debt)] ?? "")) : [];
  const dateValid = open !== null && /^\d{4}-\d{2}-\d{2}$/.test(open.paidOn) && open.paidOn >= earliestPaidOn && open.paidOn <= today;
  const valid = dateValid && amountsKop.every((amountKop) => amountKop !== null && amountKop > 0);

  function record() {
    if (recording.current || !open || !valid) {
      return;
    }

    recording.current = true;
    setOutcome(null);
    recordPayments.mutate(
      debts.map((debt, index) => ({
        paidOn: open.paidOn,
        kind: debt.kind,
        amountKop: amountsKop[index]!,
        ...recordedPeriodOf(debt),
        note: null,
      })),
      {
        onSuccess: ({ saved, failed }) => {
          if (failed.length === 0) {
            setDraft(null);
            return;
          }

          setOutcome({ saved, failed });
          setDraft({ ...open, debts: debts.filter((debt) => failed.includes(debt.kind)), retry: true });
        },
        onSettled: () => {
          recording.current = false;
        },
      },
    );
  }

  return (
    <div className="flex flex-col gap-2">
      {open ? (
        <form
          className="flex flex-col gap-3 rounded-lg border p-3"
          onSubmit={(event) => {
            event.preventDefault();
            record();
          }}
        >
          <p className="text-sm">{t("confirmMarkPaid", { count: debts.length })}</p>
          <TextField
            id="mark-paid-date"
            label={t("paidOn")}
            type="date"
            min={earliestPaidOn}
            max={today}
            value={open.paidOn}
            onChange={(paidOn) => setDraft({ ...open, paidOn })}
            errors={dateValid ? undefined : [t("paidOnInvalid")]}
          />
          {debts.map((debt, index) => (
            <TextField
              key={debt.kind}
              id={`mark-paid-amount-${debt.kind}`}
              label={debts.length > 1 ? t("paidAmountOf", { kind: tKinds(debt.kind) }) : t("paidAmount")}
              inputMode="decimal"
              autoComplete="off"
              value={open.amounts[debtKey(debt)] ?? ""}
              onChange={(text) => setDraft({ ...open, amounts: { ...open.amounts, [debtKey(debt)]: text } })}
              errors={amountsKop[index] !== null && amountsKop[index]! > 0 ? undefined : [t("paidAmountInvalid")]}
            />
          ))}
          <div className="flex flex-wrap gap-2">
            <Button type="submit" disabled={disabled || !valid}>
              {t("confirm")}
            </Button>
            <Button type="button" variant="outline" onClick={() => {
                setOutcome(null);
                setDraft(null);
              }}
              disabled={recordPayments.isPending}>
              {t("cancel")}
            </Button>
          </div>
        </form>
      ) : (
        <Button onClick={openDraft} disabled={disabled}>
          {t("markPaid")}
        </Button>
      )}
      {outcome ? (
        <p role="alert" className="text-sm text-destructive">
          {outcome.saved.length > 0
            ? t("markPaidPartial", { saved: kindList(outcome.saved), failed: kindList(outcome.failed) })
            : t("markPaidFailed")}
        </p>
      ) : null}
    </div>
  );
}
