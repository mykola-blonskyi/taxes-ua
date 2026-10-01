"use client";

import { useLocale, useTranslations } from "next-intl";
import { paymentKinds, type PaymentKind } from "@/data/payments/usePayments";
import type { PeriodsResponse } from "@/data/periods/usePeriods";
import { formatMoney } from "@/shared/lib/money";
import { cn } from "@/shared/lib/utils";
import { monthName } from "../period";

type Balances = NonNullable<PeriodsResponse["balances"]>;

const kindField = {
  SingleTax: "singleTax",
  MilitaryLevy: "militaryLevy",
  Esv: "esv",
} as const satisfies Record<PaymentKind, keyof Balances>;

export function BalancesPanel({ year, balances }: { year: number; balances: Balances }) {
  const t = useTranslations("payments.balances");
  const tKinds = useTranslations("payments.kinds");
  const locale = useLocale();

  return (
    <section className="flex flex-col gap-2">
      <h3 className="text-base font-semibold">{t("title", { year })}</h3>
      <p className="text-xs text-muted-foreground">{t("hint")}</p>
      <ul className="grid gap-2 md:grid-cols-3">
        {paymentKinds.map((kind) => {
          const balance = balances[kindField[kind]];
          const earlierOwedKop = Number(balance.earlierOwedKop);
          const owedKop = Number(balance.owedKop);
          const creditKop = Number(balance.creditKop);

          return (
            <li key={kind} className="flex min-w-0 flex-col gap-2 rounded-lg border p-3">
              <h4 className="text-sm font-semibold">{tKinds(kind)}</h4>
              <dl className="grid grid-cols-2 gap-x-2 gap-y-1 text-sm">
                {earlierOwedKop > 0 ? (
                  <>
                    <dt className="text-muted-foreground">{t("earlierOwed")}</dt>
                    <dd className="text-right tabular-nums">{formatMoney(earlierOwedKop, locale)}</dd>
                  </>
                ) : null}
                <dt className="text-muted-foreground">{t("accrued")}</dt>
                <dd className="text-right tabular-nums">{formatMoney(Number(balance.accruedKop), locale)}</dd>
                <dt className="text-muted-foreground">{t("paid")}</dt>
                <dd className="text-right tabular-nums">{formatMoney(Number(balance.paidKop), locale)}</dd>
                <dt className="font-medium">{owedKop > 0 ? t("owed") : creditKop > 0 ? t("overpaid") : t("balance")}</dt>
                <dd
                  className={cn(
                    "text-right font-semibold tabular-nums",
                    owedKop > 0 ? "text-destructive" : creditKop > 0 ? "text-emerald-700 dark:text-emerald-400" : undefined,
                  )}
                >
                  {formatMoney(owedKop > 0 ? owedKop : creditKop, locale)}
                </dd>
              </dl>
              {owedKop === 0 && creditKop === 0 && Number(balance.accruedKop) > 0 ? (
                <p className="text-xs text-muted-foreground">{t("settled")}</p>
              ) : null}
              {creditKop > 0 ? <p className="text-xs text-muted-foreground">{t("carryForward")}</p> : null}
            </li>
          );
        })}
      </ul>
      {balances.outsideGroup3Payments.length > 0 ? (
        <OutsideGroup3Payments payments={balances.outsideGroup3Payments} />
      ) : null}
    </section>
  );
}

function OutsideGroup3Payments({ payments }: { payments: Balances["outsideGroup3Payments"] }) {
  const t = useTranslations("payments");
  const locale = useLocale();

  return (
    <div className="flex flex-col gap-2 rounded-lg border border-dashed p-3">
      <h4 className="text-sm font-semibold">{t("balances.outsideGroup3.title")}</h4>
      <p className="text-xs text-muted-foreground">{t("balances.outsideGroup3.hint")}</p>
      <ul className="flex flex-col gap-1 text-sm">
        {payments.map((payment, index) => (
          <li key={index} className="flex min-w-0 items-baseline justify-between gap-2">
            <span className="min-w-0">
              {t(`kinds.${payment.kind}`)},{" "}
              {payment.month !== null
                ? monthName(Number(payment.month), locale)
                : t("quarter", { quarter: Number(payment.quarter) })}
            </span>
            <span className="shrink-0 tabular-nums">{formatMoney(Number(payment.amountKop), locale)}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
