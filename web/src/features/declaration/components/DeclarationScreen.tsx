"use client";

import Link from "next/link";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import { useDeclaration, type DeclarationResponse } from "@/data/declarations/useDeclarations";
import { declarationHref } from "@/shared/constants/navigation";
import { todayInKyiv } from "@/shared/lib/dates";
import { cn } from "@/shared/lib/utils";
import { DeadlineDate } from "@/shared/ui/DeadlineDate";
import { daysBetween, isAfter, lastEndedQuarter, periodFrom, shiftQuarter, type Period } from "../period";
import { FillInCabinet } from "./FillInCabinet";
import { FilingMark } from "./FilingMark";
import { Readiness } from "./Readiness";
import { XmlFile } from "./XmlFile";

export function DeclarationScreen({ year, quarter }: { year?: string; quarter?: string }) {
  const t = useTranslations("declaration");
  const today = todayInKyiv();
  const period = periodFrom(year, quarter, today);
  const previous = shiftQuarter(period, -1);
  const next = shiftQuarter(period, 1);
  const { data, isLoading, error } = useDeclaration(period.year, period.quarter);

  return (
    <section className="flex min-w-0 flex-col gap-6">
      <div className="flex flex-col gap-2">
        <h2 className="text-lg font-semibold md:text-xl">{t("title", period)}</h2>
        <nav className="flex flex-wrap justify-between gap-2 text-sm">
          <QuarterLink period={previous} label={t("previous")} direction="previous" />
          {isAfter(next, lastEndedQuarter(today)) ? null : (
            <QuarterLink period={next} label={t("next")} direction="next" />
          )}
        </nav>
      </div>

      {isLoading ? <p className="text-sm text-muted-foreground">{t("loading")}</p> : null}
      {error instanceof ApiError && error.status === 404 ? (
        <div className="flex flex-col gap-2">
          <p className="text-sm text-muted-foreground">{t("unavailable")}</p>
          <Link href="/settings" className="text-sm font-medium text-primary underline-offset-4 hover:underline">
            {t("unavailableCta")}
          </Link>
        </div>
      ) : error ? (
        <p className="text-sm text-destructive">{t("loadFailed")}</p>
      ) : null}

      {data ? <Declaration declaration={data} period={period} today={today} /> : null}
    </section>
  );
}

function QuarterLink({ period, label, direction }: { period: Period; label: string; direction: "previous" | "next" }) {
  return (
    <Link
      href={declarationHref(period.year, period.quarter)}
      className={cn(
        "inline-flex items-center gap-1 font-medium text-primary underline-offset-4 hover:underline",
        direction === "next" && "ml-auto",
      )}
    >
      {direction === "previous" ? <ChevronLeft className="size-4" aria-hidden /> : null}
      {label}
      {direction === "next" ? <ChevronRight className="size-4" aria-hidden /> : null}
    </Link>
  );
}

function Declaration({ declaration, period, today }: { declaration: DeclarationResponse; period: Period; today: string }) {
  const t = useTranslations("declaration");
  const locale = useLocale();
  const daysLeft = daysBetween(today, declaration.filing.due);

  return (
    <>
      <dl className="grid grid-cols-2 gap-4 rounded-xl border bg-card p-4 text-sm">
        <div className="flex min-w-0 flex-col gap-1">
          <dt className="text-muted-foreground">{t("fileBy")}</dt>
          <dd className="font-medium">
            <DeadlineDate deadline={declaration.filing} locale={locale} />
            {declaration.filed || daysLeft < 0 ? null : (
              <span className="text-xs font-normal text-amber-700 dark:text-amber-400">
                {daysLeft === 0 ? t("dueToday") : t("daysLeft", { days: daysLeft })}
              </span>
            )}
          </dd>
        </div>
        <div className="flex min-w-0 flex-col gap-1">
          <dt className="text-muted-foreground">{t("payBy")}</dt>
          <dd className="font-medium">
            <DeadlineDate deadline={declaration.payment} locale={locale} />
          </dd>
        </div>
      </dl>

      <FilingMark
        filed={declaration.filed}
        period={period}
        today={today}
        group3Confirmed={declaration.readiness.group3Confirmed}
      />
      <Readiness year={period.year} readiness={declaration.readiness} />
      <FillInCabinet declaration={declaration} />
      <XmlFile declaration={declaration} period={period} />
    </>
  );
}
