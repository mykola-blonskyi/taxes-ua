"use client";

import type { ReactNode } from "react";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { useDashboard, type DashboardResponse, type KindDebt } from "@/data/dashboard/useDashboard";
import { declarationHref } from "@/shared/constants/navigation";
import { formatMoney, formatRate } from "@/shared/lib/money";
import { formatLongDate } from "./debt";
import { DaysLeft, DebtPeriod } from "./DebtParts";
import { Group3Status } from "./Group3Status";
import { HeroCard } from "./HeroCard";
import { LoadState } from "@/data/api/LoadState";
import { LimitBar } from "./LimitBar";
import { activeNotices, isGroup3Unconfirmed, mostSevere, type Notice } from "./notices";
import { PayDebtButton } from "./PayDebtButton";
import { ReserveCard } from "./ReserveCard";
import { SyncHealth } from "./SyncHealth";

export function DashboardScreen() {
  const t = useTranslations("dashboard");
  const query = useDashboard();
  const { data, isFetching } = query;

  if (query.isLoading || query.isError || !data) {
    return <LoadState query={query} loading={t("loading")} failed={t("loadFailed")} />;
  }

  const { nextStep, today } = data;
  const [banner, ...folded] = activeNotices(data);
  const notice = (name: Notice) => <NoticeView key={name} name={name} data={data} />;

  return (
    <div className="flex flex-col gap-6">
      {banner ? notice(banner) : null}
      {nextStep.state === "Pay" ? (
        <>
          <HeroCard now={nextStep.now} today={today} busy={isFetching} />
          {nextStep.later.length > 0 ? <LaterDebts debts={nextStep.later} today={today} /> : null}
        </>
      ) : (
        <StateCard response={data} />
      )}
      {folded.length > 0 ? (
        <FoldedNotices notices={folded} data={data}>
          {folded.map(notice)}
        </FoldedNotices>
      ) : null}
      {data.sync?.state === "Healthy" ? <SyncHealth sync={data.sync} /> : null}
      {data.reserve ? (
        <ReserveCard reserve={data.reserve} today={today} limitCrossing={data.limitCrossing} />
      ) : null}
      {data.credits.length > 0 ? <Credits credits={data.credits} /> : null}
      {data.burden ? <Burden burden={data.burden} /> : null}
      {data.limit ? <LimitBar limit={data.limit} /> : null}
      <InvoicesLink />
    </div>
  );
}

function NoticeView({ name, data }: { name: Notice; data: DashboardResponse }) {
  switch (name) {
    case "overdueInvoices":
      return <OverdueInvoicesNotice count={Number(data.overdueInvoiceCount)} />;
    case "syncBroken":
    case "syncStale":
      return data.sync ? <SyncHealth sync={data.sync} /> : null;
    case "group3":
      return <Group3Status group3={data.group3} today={data.today} />;
    case "limitCrossing":
      return data.limitCrossing ? <LimitCrossingWarning crossing={data.limitCrossing} /> : null;
    case "declaration":
      return data.declaration ? <DeclarationDue due={data.declaration} today={data.today} /> : null;
    case "review":
      return <ReviewWarning count={Number(data.needsReviewCount)} />;
  }
}

// One short line per notice, for the folded list's summary.
function useNoticeTitles(data: DashboardResponse): Record<Notice, string> {
  const t = useTranslations("dashboard");
  const tDeclaration = useTranslations("declaration");
  const syncKey =
    data.sync?.state === "Stale" ? "stale" : data.sync?.state === "TokenUnreadable" ? "tokenUnreadable" : "tokenRejected";
  const crossing = data.limitCrossing;
  const due = data.declaration;

  return {
    syncBroken: t(`sync.${syncKey}.title`),
    syncStale: t("sync.stale.title"),
    limitCrossing: crossing
      ? t("limitCrossing.title", { quarter: Number(crossing.quarter), year: Number(crossing.year) })
      : "",
    group3: isGroup3Unconfirmed(data.group3) ? t("group3.unconfirmedShort") : t("group3.beforeGroup3Title"),
    declaration: due ? tDeclaration("title", { quarter: Number(due.quarter), year: Number(due.year) }) : "",
    review: t("review.title", { count: Number(data.needsReviewCount) }),
    overdueInvoices: t("overdueInvoices.title", { count: Number(data.overdueInvoiceCount) }),
  };
}

// Everything but the single banner waits here, under the hero, until the owner opens it.
// The summary borrows the colour of the most severe notice it holds, so a rejected bank token cannot hide
// behind a neutral line, and it names them so the owner knows what is inside without opening it.
const summaryTone = {
  alert: "border-destructive/40 bg-destructive/10 text-destructive",
  warning: "border-amber-500/50 bg-amber-500/10 text-amber-800 dark:text-amber-300",
  info: "bg-muted text-foreground",
} as const;

function FoldedNotices({
  notices,
  data,
  children,
}: {
  notices: Notice[];
  data: DashboardResponse;
  children: ReactNode;
}) {
  const t = useTranslations("dashboard");
  const titles = useNoticeTitles(data);
  const severity = mostSevere(notices);

  return (
    <details className="group rounded-lg border" data-severity={severity}>
      <summary
        className={`flex min-h-11 cursor-pointer flex-col justify-center gap-0.5 rounded-lg px-4 py-2 text-sm ${summaryTone[severity]}`}
      >
        <span className="font-medium">{t("attention", { count: notices.length })}</span>
        <span className="break-words text-xs font-normal">{notices.map((notice) => titles[notice]).join(" · ")}</span>
      </summary>
      <div className="flex flex-col gap-3 p-3 pt-0">{children}</div>
    </details>
  );
}

function StateCard({ response }: { response: DashboardResponse }) {
  const t = useTranslations("dashboard");
  const tPeriods = useTranslations("periods");
  const locale = useLocale();
  const { nextStep } = response;

  switch (nextStep.state) {
    case "AllDone":
      return <Card title={t("allDone.title")} text={t("allDone.text")} tone="clear" />;
    case "BeforeRegistration":
      return (
        <Card
          title={t("beforeRegistration.title")}
          text={t("beforeRegistration.text", { date: formatLongDate(nextStep.registrationDate!, response.today, locale) })}
        />
      );
    case "RegistrationDateNotSet":
      return (
        <Card title={t("registrationDateNotSet.title")} text={t("registrationDateNotSet.text")}>
          <SettingsLink />
        </Card>
      );
    case "MissingTaxYear":
      return (
        <Card title={t("missingTaxYear")} text={tPeriods("warnings.missingTaxYear", { year: Number(nextStep.missingTaxYear) })}>
          <SettingsLink />
        </Card>
      );
    case "Pay":
      return null;
  }
}

function Card({
  title,
  text,
  tone,
  children,
}: {
  title: string;
  text: string;
  tone?: "clear";
  children?: ReactNode;
}) {
  return (
    <section className="flex flex-col gap-2 rounded-xl border bg-card p-4 md:p-6">
      <h3
        className={
          tone === "clear"
            ? "text-2xl font-semibold text-emerald-700 dark:text-emerald-400"
            : "text-lg font-semibold"
        }
      >
        {title}
      </h3>
      <p className="text-sm text-muted-foreground">{text}</p>
      {children}
    </section>
  );
}

function LimitCrossingWarning({ crossing }: { crossing: NonNullable<DashboardResponse["limitCrossing"]> }) {
  const t = useTranslations("dashboard.limitCrossing");

  return (
    <section
      role="alert"
      className="flex min-w-0 flex-col gap-2 rounded-lg border border-destructive/40 bg-destructive/10 p-4"
    >
      <h3 className="break-words text-sm font-semibold text-destructive">
        {t("title", { quarter: Number(crossing.quarter), year: Number(crossing.year) })}
      </h3>
      <p className="break-words text-sm text-destructive">
        {t("text", {
          switchQuarter: Number(crossing.switchFromQuarter),
          switchYear: Number(crossing.switchFromYear),
        })}
      </p>
      {crossing.backOnGroup3From ? (
        <p className="break-words text-sm text-muted-foreground">
          {t("back", {
            quarter: Number(crossing.backOnGroup3From.quarter),
            year: Number(crossing.backOnGroup3From.year),
          })}
        </p>
      ) : null}
    </section>
  );
}

function ReviewWarning({ count }: { count: number }) {
  const t = useTranslations("dashboard.review");

  return (
    <section className="flex flex-col gap-2 rounded-lg border bg-muted p-4">
      <h3 className="text-sm font-semibold text-destructive">{t("title", { count })}</h3>
      <p className="text-sm text-muted-foreground">{t("text", { count })}</p>
      <Link href="/review" className="text-sm font-medium text-primary underline-offset-4 hover:underline">
        {t("cta")}
      </Link>
    </section>
  );
}

function DeclarationDue({ due, today }: { due: NonNullable<DashboardResponse["declaration"]>; today: string }) {
  const t = useTranslations("dashboard.declaration");
  const tDeclaration = useTranslations("declaration");
  const locale = useLocale();
  const year = Number(due.year);
  const quarter = Number(due.quarter);

  return (
    <section className="flex flex-col gap-2 rounded-lg border bg-muted p-4">
      <h3 className="text-sm font-semibold">{tDeclaration("title", { quarter, year })}</h3>
      <p className="text-sm text-muted-foreground">
        {t("fileBy", { date: formatLongDate(due.dueDate, today, locale) })}
        {" · "}
        <DaysLeft days={Number(due.daysLeft)} />
      </p>
      <Link
        href={declarationHref(year, quarter)}
        className="text-sm font-medium text-primary underline-offset-4 hover:underline"
      >
        {t("cta")}
      </Link>
    </section>
  );
}

function OverdueInvoicesNotice({ count }: { count: number }) {
  const t = useTranslations("dashboard.overdueInvoices");

  return (
    <section className="flex flex-col gap-2 rounded-lg border bg-muted p-4">
      <h3 className="text-sm font-semibold text-destructive">{t("title", { count })}</h3>
      <Link href="/invoices" className="text-sm font-medium text-primary underline-offset-4 hover:underline">
        {t("cta")}
      </Link>
    </section>
  );
}

function SettingsLink() {
  const t = useTranslations("dashboard");

  return (
    <Link href="/settings" className="text-sm font-medium text-primary underline-offset-4 hover:underline">
      {t("settingsCta")}
    </Link>
  );
}

function InvoicesLink() {
  const t = useTranslations("dashboard.invoices");

  return (
    <section className="flex flex-col gap-2 rounded-xl border bg-card p-4">
      <h3 className="text-base font-semibold">{t("title")}</h3>
      <p className="text-sm text-muted-foreground">{t("text")}</p>
      <Link href="/invoices" className="text-sm font-medium text-primary underline-offset-4 hover:underline">
        {t("cta")}
      </Link>
    </section>
  );
}

function LaterDebts({ debts, today }: { debts: KindDebt[]; today: string }) {
  const t = useTranslations("dashboard");
  const tKinds = useTranslations("payments.kinds");
  const locale = useLocale();

  return (
    <section className="flex flex-col gap-2">
      <h3 className="text-base font-semibold">{t("later")}</h3>
      <ul className="flex flex-col divide-y rounded-lg border">
        {debts.map((debt) => (
          <li
            key={debt.kind}
            className="grid grid-cols-[minmax(0,1fr)_auto] items-baseline gap-x-4 p-3"
          >
            <div className="flex min-w-0 flex-col">
              <span className="text-sm font-medium">
                {tKinds(debt.kind)}, <DebtPeriod debt={debt} />
              </span>
              <span className="text-xs text-muted-foreground">
                {formatLongDate(debt.dueDate, today, locale)}
                {" · "}
                <DaysLeft days={Number(debt.daysLeft)} />
              </span>
            </div>
            <span className="font-semibold tabular-nums">{formatMoney(Number(debt.amountKop), locale)}</span>
            <PayDebtButton debt={debt} className="col-span-full mt-2 w-fit" />
          </li>
        ))}
      </ul>
    </section>
  );
}

function Burden({ burden }: { burden: NonNullable<DashboardResponse["burden"]> }) {
  const t = useTranslations("dashboard.burden");
  const locale = useLocale();

  return (
    <section className="flex flex-col gap-1">
      <h3 className="text-base font-semibold">{t("title")}</h3>
      {burden.rateBp === null ? (
        <p className="text-sm text-muted-foreground">{t("noIncome")}</p>
      ) : (
        <>
          <p className="text-3xl font-semibold tabular-nums">{formatRate(Number(burden.rateBp), locale)}</p>
          <p className="text-sm text-muted-foreground">
            {t("detail", {
              tax: formatMoney(Number(burden.taxKop), locale),
              income: formatMoney(Number(burden.incomeKop), locale),
            })}
          </p>
        </>
      )}
    </section>
  );
}

function Credits({ credits }: { credits: DashboardResponse["credits"] }) {
  const t = useTranslations("dashboard.credit");
  const tKinds = useTranslations("payments.kinds");
  const locale = useLocale();

  return (
    <section className="flex flex-col gap-2">
      <h3 className="text-base font-semibold">{t("title")}</h3>
      <ul className="flex flex-col divide-y rounded-lg border">
        {credits.map((credit) => (
          <li key={credit.kind} className="grid grid-cols-[minmax(0,1fr)_auto] items-baseline gap-x-4 p-3">
            <span className="text-sm font-medium">{tKinds(credit.kind)}</span>
            <span className="font-semibold tabular-nums text-emerald-700 dark:text-emerald-400">
              {formatMoney(Number(credit.creditKop), locale)}
            </span>
          </li>
        ))}
      </ul>
      <p className="text-xs text-muted-foreground">{t("hint")}</p>
    </section>
  );
}

