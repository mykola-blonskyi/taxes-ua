"use client";

import { useState } from "react";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import { recordedPeriodOf, useDashboard, type DashboardResponse } from "@/data/dashboard/useDashboard";
import {
  paymentKinds,
  useConfirmCandidate,
  useDismissCandidate,
  usePaymentCandidates,
  type PaymentCandidate,
  type PaymentKind,
} from "@/data/payments/usePayments";
import { useDismissTreasuryNotice } from "@/data/treasury/useTreasuryAccounts";
import { formatDateOnly } from "@/shared/lib/dates";
import { formatMoney } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";
import { SelectField } from "@/shared/ui/fields";
import { fromPeriodValue, monthName, toPeriodValue, type PeriodValue } from "../period";
import { PeriodSelect } from "./PeriodSelect";

type CandidateState = { kind: PaymentKind | null; periodYear: number; period: PeriodValue };

type ConfirmedNotice = { candidate: PaymentCandidate; kind: PaymentKind; manualIban: string };

// What the home screen would record for this kind's debt, else the quarter the payment was made in.
function defaultPeriod(
  dashboard: DashboardResponse | undefined,
  kind: PaymentKind | null,
  paidOn: string,
): Pick<CandidateState, "periodYear" | "period"> {
  const debts = dashboard ? [...dashboard.nextStep.now, ...dashboard.nextStep.later] : [];
  const debt = kind ? debts.find((candidate) => candidate.kind === kind) : undefined;

  if (debt) {
    const recorded = recordedPeriodOf(debt);

    return { periodYear: recorded.periodYear, period: toPeriodValue(recorded) };
  }

  const [year, month] = paidOn.split("-").map(Number);

  return { periodYear: year, period: `q${Math.ceil(month / 3)}` };
}

export function PaymentCandidates() {
  const t = useTranslations("payments.candidates");
  const candidates = usePaymentCandidates();
  const dashboard = useDashboard();
  // Kept here: a confirmed card leaves the list as soon as it reloads.
  const [notices, setNotices] = useState<ConfirmedNotice[]>([]);

  if (candidates.isLoading || dashboard.isLoading) {
    return <p className="text-sm text-muted-foreground">{t("loading")}</p>;
  }

  if (candidates.isError || !candidates.data) {
    return <p className="text-sm text-destructive">{t("loadFailed")}</p>;
  }

  const closeNotice = (notice: ConfirmedNotice) => setNotices((current) => current.filter((shown) => shown !== notice));

  return (
    <div className="flex flex-col gap-4">
      {candidates.data.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t("empty")}</p>
      ) : (
        <p className="text-sm text-muted-foreground">{t("intro")}</p>
      )}
      {notices.length + candidates.data.length > 0 ? (
        <ul className="grid gap-4 sm:grid-cols-2">
          {notices.map((notice) => (
            <NoticeCard key={notice.candidate.id} notice={notice} onClose={() => closeNotice(notice)} />
          ))}
          {candidates.data.map((candidate) => (
            // A kind the owner confirms for an account becomes the suggestion for its other candidates, and
            // a card whose suggestion moved starts again from it.
            <CandidateCard
              key={`${candidate.id}-${candidate.suggestedKind}`}
              candidate={candidate}
              dashboard={dashboard.data}
              onNotice={(notice) => setNotices((current) => [notice, ...current])}
            />
          ))}
        </ul>
      ) : null}
    </div>
  );
}

function NoticeCard({ notice, onClose }: { notice: ConfirmedNotice; onClose: () => void }) {
  const t = useTranslations("payments.candidates.otherAccount");
  const tPayments = useTranslations("payments");
  const locale = useLocale();
  const dismiss = useDismissTreasuryNotice();

  return (
    <li
      role="status"
      className="flex min-w-0 flex-col gap-2 rounded-lg border border-amber-500/50 bg-amber-500/10 p-3 text-sm"
    >
      <p className="min-w-0 break-words">
        {t("text", {
          date: formatDateOnly(notice.candidate.paidOn, locale),
          amount: formatMoney(Number(notice.candidate.amountKop), locale),
          kind: tPayments(`kinds.${notice.kind}`),
        })}
      </p>
      <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-0.5 text-xs">
        <dt className="text-muted-foreground">{t("paidTo")}</dt>
        <dd className="break-all font-mono">{notice.candidate.counterIban}</dd>
        <dt className="text-muted-foreground">{t("yours")}</dt>
        <dd className="break-all font-mono">{notice.manualIban}</dd>
      </dl>
      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={dismiss.isPending}
          onClick={() => dismiss.mutate(notice.kind, { onSuccess: onClose })}
        >
          {t("dismiss")}
        </Button>
        <Link href="/settings?tab=treasury" className="text-sm text-primary underline-offset-4 hover:underline">
          {t("settings")}
        </Link>
      </div>
      {dismiss.isError ? (
        <p role="alert" className="text-destructive">
          {t("dismissFailed")}
        </p>
      ) : null}
    </li>
  );
}

function CandidateCard({
  candidate,
  dashboard,
  onNotice,
}: {
  candidate: PaymentCandidate;
  dashboard: DashboardResponse | undefined;
  onNotice: (notice: ConfirmedNotice) => void;
}) {
  const t = useTranslations("payments.candidates");
  const tPayments = useTranslations("payments");
  const locale = useLocale();
  const confirmCandidate = useConfirmCandidate();
  const dismissCandidate = useDismissCandidate();
  const [confirmingDismiss, setConfirmingDismiss] = useState(false);
  const [state, setState] = useState<CandidateState>(() => ({
    kind: candidate.suggestedKind,
    ...defaultPeriod(dashboard, candidate.suggestedKind, candidate.paidOn),
  }));

  const match = candidate.matches.find((typed) => typed.kind === state.kind);
  const pending = confirmCandidate.isPending || dismissCandidate.isPending;
  const failure = [confirmCandidate.error, dismissCandidate.error].find((error) => error instanceof ApiError) as
    | ApiError
    | undefined;
  const paidYear = Number(candidate.paidOn.slice(0, 4));
  const yearOptions = [...new Set([state.periodYear, paidYear, paidYear - 1])].sort((a, b) => a - b);
  const formattedAmount = formatMoney(Number(candidate.amountKop), locale);
  const cardName = `${formatDateOnly(candidate.paidOn, locale)}, ${formattedAmount}`;

  function chooseKind(value: string) {
    const kind = value as PaymentKind;

    setState({ kind, ...defaultPeriod(dashboard, kind, candidate.paidOn) });
  }

  function confirm(recordSeparately = false) {
    const kind = state.kind;

    if (!kind) {
      return;
    }

    // The promise, not mutate's callbacks: those are dropped once the reloaded list unmounts this card. A
    // failure is shown from the mutation's own error.
    confirmCandidate
      .mutateAsync({
        id: candidate.id,
        body: {
          kind,
          periodYear: state.periodYear,
          ...fromPeriodValue(state.period),
          linkPaymentId: recordSeparately ? null : (match?.id ?? null),
          recordSeparately,
        },
      })
      .then((confirmed) => {
        if (confirmed?.notice) {
          onNotice({ candidate, kind, manualIban: confirmed.notice.manualIban });
        }
      })
      .catch(() => {});
  }

  return (
    <li className="flex min-w-0 flex-col gap-3 rounded-lg border p-3" aria-label={cardName}>
      <div className="flex items-start justify-between gap-2">
        <span className="text-sm font-medium">{formatDateOnly(candidate.paidOn, locale)}</span>
        <span className="shrink-0 text-sm font-semibold">{formattedAmount}</span>
      </div>

      <div className="flex min-w-0 flex-col gap-0.5 text-xs text-muted-foreground">
        {candidate.counterName ? <span className="min-w-0 break-words text-sm text-foreground">{candidate.counterName}</span> : null}
        <span className="min-w-0 break-all">{candidate.counterIban}</span>
        {candidate.purpose ? <span className="min-w-0 break-words">{candidate.purpose}</span> : null}
      </div>

      <div className="grid gap-3 sm:grid-cols-2">
        <div className="min-w-0 sm:col-span-2">
          <SelectField
            id={`candidate-kind-${candidate.id}`}
            label={t("kind")}
            value={state.kind ?? ""}
            onChange={chooseKind}
            placeholder={t("chooseKind")}
            options={paymentKinds.map((kind) => ({ value: kind, label: tPayments(`kinds.${kind}`) }))}
          />
        </div>
        <div className="min-w-0">
          <SelectField
            id={`candidate-year-${candidate.id}`}
            label={tPayments("form.periodYear")}
            value={String(state.periodYear)}
            onChange={(value) => setState((current) => ({ ...current, periodYear: Number(value) }))}
            options={yearOptions.map((year) => ({ value: String(year), label: String(year) }))}
          />
        </div>
        <div className="min-w-0">
          <PeriodSelect
            id={`candidate-period-${candidate.id}`}
            year={state.periodYear}
            value={state.period}
            onChange={(period) => setState((current) => ({ ...current, period }))}
          />
        </div>
      </div>

      {match ? (
        <div className="flex min-w-0 flex-col gap-0.5 rounded-lg border border-primary/40 bg-primary/5 p-2 text-xs">
          <p>{t("matchNotice")}</p>
          <p className="text-muted-foreground">
            {tPayments("periodOf", {
              period:
                match.periodMonth !== null
                  ? monthName(Number(match.periodMonth), locale)
                  : tPayments("quarter", { quarter: Number(match.periodQuarter) }),
              year: Number(match.periodYear),
            })}
          </p>
          {match.note ? <p className="min-w-0 break-words text-muted-foreground">{match.note}</p> : null}
        </div>
      ) : null}

      <div className="flex flex-wrap items-center gap-2">
        {confirmingDismiss ? (
          <>
            <span className="text-xs text-destructive">{t("confirmDismiss")}</span>
            <Button
              type="button"
              variant="destructive"
              size="sm"
              disabled={pending}
              onClick={() => dismissCandidate.mutate(candidate.id)}
            >
              {t("confirmDismissYes")}
            </Button>
            <Button type="button" variant="outline" size="sm" onClick={() => setConfirmingDismiss(false)}>
              {t("confirmDismissCancel")}
            </Button>
          </>
        ) : (
          <>
            <Button
              type="button"
              size="sm"
              aria-label={`${match ? t("link") : t("confirm")}: ${cardName}`}
              disabled={!state.kind || pending}
              onClick={() => confirm()}
            >
              {match ? t("link") : t("confirm")}
            </Button>
            {match ? (
              <Button
                type="button"
                variant="outline"
                size="sm"
                aria-label={`${t("recordSeparately")}: ${cardName}`}
                disabled={!state.kind || pending}
                onClick={() => confirm(true)}
              >
                {t("recordSeparately")}
              </Button>
            ) : null}
            <Button
              type="button"
              variant="outline"
              size="sm"
              aria-label={`${t("dismiss")}: ${cardName}`}
              disabled={pending}
              onClick={() => setConfirmingDismiss(true)}
            >
              {t("dismiss")}
            </Button>
          </>
        )}
      </div>

      {failure ? (
        <p className="text-xs text-destructive">
          {failure.status === 409
            ? t("stale")
            : failure.status === 400
              ? t("invalid")
              : `${t("failed")} ${failure.message}`}
        </p>
      ) : null}
    </li>
  );
}
