"use client";

import { useState, type FormEvent } from "react";
import { useLocale, useTranslations } from "next-intl";
import { problemOf } from "@/data/api/client";
import { useApiErrorText } from "@/data/api/useApiErrorText";
import {
  useDismissTreasuryNotice,
  useRevertTreasuryAccount,
  useSaveTreasuryAccount,
  useTreasuryAccounts,
  type TreasuryAccount,
} from "@/data/treasury/useTreasuryAccounts";
import { formatDateOnly } from "@/shared/lib/dates";
import { Button } from "@/shared/ui/button";
import { LoadState } from "@/data/api/LoadState";
import { TextField } from "@/shared/ui/fields";
import { TreasuryValidUntil } from "./TreasuryValidUntil";

function normalizeIban(value: string): string {
  return value.replace(/\s/g, "").toUpperCase();
}

type FormState = { iban: string; recipientName: string; recipientCode: string };

export function TreasuryAccountsSection() {
  const t = useTranslations("settings.treasury");
  const query = useTreasuryAccounts();
  const { data } = query;

  if (!data) {
    return <LoadState query={query} loading={t("loading")} failed={t("loadFailed")} />;
  }

  return (
    <div className="flex max-w-xl flex-col gap-4">
      <p className="text-sm text-muted-foreground">{t("hint")}</p>
      <ul className="flex flex-col gap-3">
        {data.map((account) => (
          <li key={account.kind} className="min-w-0 rounded-lg border p-3">
            <AccountCard account={account} />
          </li>
        ))}
      </ul>
    </div>
  );
}

function AccountCard({ account }: { account: TreasuryAccount }) {
  const t = useTranslations("settings.treasury");
  const tKinds = useTranslations("payments.kinds");
  const locale = useLocale();
  const [editing, setEditing] = useState(false);
  const revert = useRevertTreasuryAccount();
  const dismiss = useDismissTreasuryNotice();
  const apiText = useApiErrorText();
  const actionFailure = revert.error ?? dismiss.error;

  const source =
    account.source === "Manual"
      ? t("source.manual")
      : account.source === "Learned" && account.learned
        ? t("source.learned", { date: formatDateOnly(account.learned.paidOn, locale) })
        : t("source.none");

  return (
    <div className="flex min-w-0 flex-col gap-3">
      <div className="flex min-w-0 flex-col gap-0.5">
        <h3 className="font-medium">{tKinds(account.kind)}</h3>
        <span className="text-xs text-muted-foreground">{source}</span>
      </div>

      {account.notice ? (
        <div role="status" className="flex min-w-0 flex-col gap-2 rounded-lg border border-amber-500/50 bg-amber-500/10 p-3 text-sm">
          <p className="min-w-0 break-words">
            {t("notice.text", { date: formatDateOnly(account.notice.paidOn, locale) })}
          </p>
          <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-0.5 text-xs">
            <dt className="text-muted-foreground">{t("iban")}</dt>
            <dd className="break-all font-mono">{account.notice.iban}</dd>
            {account.notice.recipientName ? (
              <>
                <dt className="text-muted-foreground">{t("recipientName")}</dt>
                <dd className="break-words">{account.notice.recipientName}</dd>
              </>
            ) : null}
          </dl>
          <div className="flex flex-wrap gap-2">
            <Button type="button" variant="outline" size="sm" disabled={dismiss.isPending} onClick={() => dismiss.mutate(account.kind)}>
              {t("notice.dismiss")}
            </Button>
            <Button type="button" variant="outline" size="sm" disabled={revert.isPending} onClick={() => revert.mutate(account.kind)}>
              {t("notice.useLearned")}
            </Button>
          </div>
          {actionFailure ? (
            <p role="alert" className="text-destructive">
              {apiText.withReason(t("actionFailed"), actionFailure)}
            </p>
          ) : null}
        </div>
      ) : null}

      {editing ? (
        <AccountEditor account={account} onClose={() => setEditing(false)} />
      ) : (
        <>
          {account.iban ? (
            <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-1 text-sm">
              <dt className="text-muted-foreground">{t("iban")}</dt>
              <dd className="break-all font-mono text-xs leading-5">{account.iban}</dd>
              <dt className="text-muted-foreground">{t("recipientName")}</dt>
              <dd className="break-words">{account.recipientName ?? "—"}</dd>
              <dt className="text-muted-foreground">{t("recipientCode")}</dt>
              <dd>{account.recipientCode ?? "—"}</dd>
            </dl>
          ) : (
            <p className="text-sm text-muted-foreground">{t("empty")}</p>
          )}
          {account.missing.length > 0 ? (
            <p className="text-sm text-amber-700 dark:text-amber-400">
              {t("missing", { fields: account.missing.map((field) => t(field as "recipientName" | "recipientCode").toLowerCase()).join(", ") })}
            </p>
          ) : null}
          <TreasuryValidUntil account={account} />
          {actionFailure && !account.notice ? <p className="text-sm text-destructive">{apiText.withReason(t("actionFailed"), actionFailure)}</p> : null}
          <div className="flex flex-wrap gap-2">
            <Button type="button" variant="outline" size="sm" onClick={() => setEditing(true)}>
              {account.iban ? t("edit") : t("enter")}
            </Button>
            {account.source === "Manual" && account.hasLearned ? (
              <Button type="button" variant="outline" size="sm" disabled={revert.isPending} onClick={() => revert.mutate(account.kind)}>
                {t("revert")}
              </Button>
            ) : null}
          </div>
        </>
      )}
    </div>
  );
}

function AccountEditor({ account, onClose }: { account: TreasuryAccount; onClose: () => void }) {
  const t = useTranslations("settings.treasury");
  const apiText = useApiErrorText();
  const save = useSaveTreasuryAccount();
  const [form, setForm] = useState<FormState>({
    iban: account.iban ?? "",
    recipientName: account.recipientName ?? "",
    recipientCode: account.recipientCode ?? "",
  });
  const failure = save.error;
  const rejected = Object.keys(problemOf(failure)?.fieldCodes ?? {}).length > 0;

  function fieldErrors(field: keyof FormState): string[] | undefined {
    return problemOf(failure)?.fieldCodes[field]?.map(apiText.ofCode);
  }

  function update(field: keyof FormState) {
    return (value: string) => setForm((current) => ({ ...current, [field]: value }));
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    // The end belongs to the IBAN: it is kept only when the IBAN typed is the one in use (Rule 16).
    const validUntil = normalizeIban(form.iban) === account.iban ? account.validUntil : null;
    save.mutate({ kind: account.kind, body: { ...form, validUntil } }, { onSuccess: onClose });
  }

  return (
    <form onSubmit={submit} className="flex min-w-0 flex-col gap-3" noValidate>
      <TextField
        id={`treasury-${account.kind}-iban`}
        label={t("iban")}
        hint={t("ibanHint")}
        value={form.iban}
        onChange={update("iban")}
        errors={fieldErrors("iban")}
        autoComplete="off"
        spellCheck={false}
        className="font-mono"
      />
      <TextField
        id={`treasury-${account.kind}-name`}
        label={t("recipientName")}
        value={form.recipientName}
        onChange={update("recipientName")}
        errors={fieldErrors("recipientName")}
        autoComplete="off"
      />
      <TextField
        id={`treasury-${account.kind}-code`}
        label={t("recipientCode")}
        hint={t("recipientCodeHint")}
        value={form.recipientCode}
        onChange={update("recipientCode")}
        errors={fieldErrors("recipientCode")}
        inputMode="numeric"
        autoComplete="off"
      />
      {rejected ? (
        <p role="alert" className="text-sm text-destructive">
          {t("validationError")}
        </p>
      ) : failure ? (
        <p role="alert" className="text-sm text-destructive">
          {apiText.withReason(t("saveFailed"), failure)}
        </p>
      ) : null}
      <div className="flex flex-wrap gap-2">
        <Button type="submit" size="sm" disabled={save.isPending}>
          {save.isPending ? t("saving") : t("save")}
        </Button>
        <Button type="button" variant="outline" size="sm" onClick={onClose}>
          {t("cancel")}
        </Button>
      </div>
    </form>
  );
}
