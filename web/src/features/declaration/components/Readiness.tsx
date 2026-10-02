"use client";

import Link from "next/link";
import { CircleAlert, CircleCheck } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import type { DeclarationReadiness } from "@/data/declarations/useDeclarations";
import { paymentKinds, type PaymentKind } from "@/data/payments/usePayments";
import { formatMoney } from "@/shared/lib/money";

type Item = { key: string; met: boolean; text: string; fix?: { href: string; label: string } };

const unpaidKop: Record<PaymentKind, (unpaid: DeclarationReadiness["unpaid"]) => number> = {
  SingleTax: (unpaid) => Number(unpaid.singleTaxKop),
  MilitaryLevy: (unpaid) => Number(unpaid.militaryLevyKop),
  Esv: (unpaid) => Number(unpaid.esvKop),
};

export function Readiness({ year, readiness }: { year: number; readiness: DeclarationReadiness }) {
  const t = useTranslations("declaration.readiness");
  const tFields = useTranslations("declaration.fields");
  const receipts = Number(readiness.receiptsToReview);
  const candidates = Number(readiness.pendingPaymentCandidates);
  const missing = readiness.missingDetails;

  const items: Item[] = [
    {
      key: "receipts",
      met: receipts === 0,
      text: receipts === 0 ? t("receiptsMet") : t("receiptsUnmet", { count: receipts }),
      fix: { href: "/review", label: t("receiptsCta") },
    },
    {
      key: "candidates",
      met: candidates === 0,
      text: candidates === 0 ? t("candidatesMet") : t("candidatesUnmet", { count: candidates }),
      fix: { href: "/review", label: t("candidatesCta") },
    },
    {
      key: "taxYear",
      met: readiness.taxYearVerified,
      text: readiness.taxYearVerified ? t("taxYearMet", { year }) : t("taxYearUnmet", { year }),
      fix: { href: "/settings?tab=taxYears", label: t("taxYearCta") },
    },
    {
      key: "registration",
      met: readiness.registrationDateSet,
      text: readiness.registrationDateSet ? t("registrationMet") : t("registrationUnmet"),
      fix: { href: "/settings?tab=fop", label: t("registrationCta") },
    },
    {
      key: "details",
      met: missing.length === 0,
      text:
        missing.length === 0
          ? t("detailsMet")
          : t("detailsUnmet", { fields: missing.map((field) => tFields(field)).join(", ") }),
      fix: { href: "/settings?tab=declaration", label: t("detailsCta") },
    },
    readiness.beforeGroup3
      ? {
          key: "group3",
          met: false,
          text: t("beforeGroup3"),
          fix: { href: "/settings?tab=dps", label: t("beforeGroup3Cta") },
        }
      : {
          key: "group3",
          met: !readiness.outsideGroup3,
          text: readiness.outsideGroup3 ? t("limitUnmet") : t("limitMet"),
        },
  ];

  return (
    <section className="flex min-w-0 flex-col gap-3" aria-labelledby="readiness-heading">
      <h3 id="readiness-heading" className="text-base font-semibold">
        {t("title")}
      </h3>
      <p
        className={
          readiness.ready
            ? "text-sm font-medium text-emerald-700 dark:text-emerald-400"
            : "text-sm font-medium text-amber-700 dark:text-amber-400"
        }
      >
        {readiness.ready ? t("ready") : t("notReady")}
      </p>
      <ul className="flex flex-col divide-y rounded-lg border">
        {items.map((item) => (
          <li key={item.key} className="flex min-w-0 items-start gap-2 p-3 text-sm">
            {item.met ? (
              <CircleCheck className="mt-0.5 size-4 shrink-0 text-emerald-700 dark:text-emerald-400" aria-hidden />
            ) : (
              <CircleAlert className="mt-0.5 size-4 shrink-0 text-amber-700 dark:text-amber-400" aria-hidden />
            )}
            <div className="flex min-w-0 flex-1 flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
              <span className="min-w-0 break-words">{item.text}</span>
              {!item.met && item.fix ? (
                <Link
                  href={item.fix.href}
                  className="shrink-0 font-medium text-primary underline-offset-4 hover:underline"
                >
                  {item.fix.label}
                </Link>
              ) : null}
            </div>
          </li>
        ))}
      </ul>
      <Unpaid unpaid={readiness.unpaid} />
    </section>
  );
}

function Unpaid({ unpaid }: { unpaid: DeclarationReadiness["unpaid"] }) {
  const t = useTranslations("declaration.unpaid");
  const tKinds = useTranslations("payments.kinds");
  const locale = useLocale();
  const owed = paymentKinds.filter((kind) => unpaidKop[kind](unpaid) > 0);

  if (owed.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-col gap-2 rounded-lg border border-amber-500/40 bg-amber-500/10 p-3 text-sm">
      <p className="font-semibold text-amber-800 dark:text-amber-300">{t("title")}</p>
      <p className="text-muted-foreground">{t("text")}</p>
      <ul className="flex flex-col gap-1">
        {owed.map((kind) => (
          <li key={kind} className="flex min-w-0 justify-between gap-3">
            <span className="min-w-0">{tKinds(kind)}</span>
            <span className="whitespace-nowrap font-medium tabular-nums">
              {formatMoney(unpaidKop[kind](unpaid), locale)}
            </span>
          </li>
        ))}
      </ul>
      <Link href="/payments" className="font-medium text-primary underline-offset-4 hover:underline">
        {t("cta")}
      </Link>
    </div>
  );
}
