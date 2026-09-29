"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import {
  useDisconnectMonobank,
  useMonobankConnection,
  useSaveFollowedMonobankAccounts,
  useSaveMonobankToken,
  useSyncMonobank,
  type MonobankAccountResponse,
} from "@/data/monobank/useMonobank";
import { formatInstantInKyiv } from "@/shared/lib/dates";
import { Button } from "@/shared/ui/button";
import { CheckboxField, TextField } from "@/shared/ui/fields";

export function MonobankConnectionSection() {
  const t = useTranslations("settings.monobank");
  const { data, isLoading, isError, error } = useMonobankConnection();

  if (isLoading) {
    return <p className="text-sm text-muted-foreground">{t("loading")}</p>;
  }

  if (isError && error instanceof ApiError && error.status === 503) {
    return <p className="text-sm text-muted-foreground">{t("notConfigured")}</p>;
  }

  if (isError || !data) {
    return <p className="text-sm text-destructive">{t("loadFailed")}</p>;
  }

  return <MonobankConnectionBody connection={data} />;
}

function MonobankConnectionBody({
  connection,
}: {
  connection: { connected: boolean; accounts: MonobankAccountResponse[] };
}) {
  const t = useTranslations("settings.monobank");
  const saveToken = useSaveMonobankToken();
  const saveAccounts = useSaveFollowedMonobankAccounts();
  const disconnect = useDisconnectMonobank();
  const sync = useSyncMonobank();
  const [token, setToken] = useState("");
  const [editingToken, setEditingToken] = useState(!connection.connected);

  const tokenFailure = saveToken.error instanceof ApiError ? saveToken.error : null;
  // The one server field error an owner routinely meets, so the only one this app translates.
  const tokenErrorKeys: Record<string, string> = {
    "Token is required.": t("tokenRequired"),
    "monobank rejected this token.": t("tokenInvalid"),
  };
  const tokenErrors = tokenFailure?.errors.token?.map((message) => tokenErrorKeys[message] ?? message);
  const accountsFailure = saveAccounts.error instanceof ApiError ? saveAccounts.error : null;
  const disconnectFailure = disconnect.error instanceof ApiError ? disconnect.error : null;
  const syncFailure = sync.error instanceof ApiError ? sync.error : null;
  const fopAccounts = connection.accounts.filter((account) => account.isFop);
  const followedCount = fopAccounts.filter((account) => account.isFollowed).length;
  const syncing = connection.accounts.some((account) => account.syncPending);
  const unsupportedAccounts = connection.accounts.filter((account) => !account.isFop);

  function submitToken(event: React.FormEvent) {
    event.preventDefault();
    saveToken.mutate(token, {
      onSuccess: () => {
        setToken("");
        setEditingToken(false);
      },
    });
  }

  function cancelEditingToken() {
    setToken("");
    saveToken.reset();
    setEditingToken(false);
  }

  function toggleFollowed(externalId: string, followed: boolean) {
    const nextFollowed = new Set(fopAccounts.filter((account) => account.isFollowed).map((account) => account.externalId));
    if (followed) {
      nextFollowed.add(externalId);
    } else {
      nextFollowed.delete(externalId);
    }

    saveAccounts.mutate([...nextFollowed]);
  }

  return (
    <div className="flex max-w-xl flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2">
        <span
          className={
            connection.connected
              ? "rounded-full bg-emerald-500/15 px-2.5 py-0.5 text-xs font-medium text-emerald-600 dark:text-emerald-400"
              : "rounded-full bg-muted px-2.5 py-0.5 text-xs font-medium text-muted-foreground"
          }
        >
          {connection.connected ? t("statusConnected") : t("statusDisconnected")}
        </span>
        {connection.connected && !editingToken ? (
          <Button type="button" variant="outline" size="sm" onClick={() => setEditingToken(true)}>
            {t("replaceToken")}
          </Button>
        ) : null}
        {connection.connected ? (
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={disconnect.isPending}
            onClick={() => disconnect.mutate(undefined, { onSuccess: () => setEditingToken(true) })}
          >
            {disconnect.isPending ? t("disconnecting") : t("disconnect")}
          </Button>
        ) : null}
      </div>

      {disconnectFailure ? <p className="text-sm text-destructive">{`${t("disconnectFailed")} ${disconnectFailure.message}`}</p> : null}

      {editingToken ? (
        <form className="flex flex-col gap-2" onSubmit={submitToken}>
          <TextField
            id="monobank-token"
            label={t("token")}
            hint={t("tokenHint")}
            type="password"
            autoComplete="off"
            value={token}
            onChange={setToken}
            errors={tokenErrors}
          />
          {tokenFailure && !tokenErrors ? (
            <p className="text-sm text-destructive">{t("saveFailed")}</p>
          ) : null}
          <div className="flex items-center gap-2">
            <Button type="submit" disabled={saveToken.isPending || token.length === 0}>
              {saveToken.isPending ? t("saving") : t("save")}
            </Button>
            {connection.connected ? (
              <Button type="button" variant="outline" onClick={cancelEditingToken} disabled={saveToken.isPending}>
                {t("cancel")}
              </Button>
            ) : null}
          </div>
        </form>
      ) : null}

      {connection.connected ? (
        <div className="flex flex-wrap items-center gap-2">
          <Button
            type="button"
            size="sm"
            disabled={sync.isPending || syncing || followedCount === 0}
            onClick={() => sync.mutate()}
          >
            {sync.isPending || syncing ? t("syncing") : t("syncNow")}
          </Button>
          {followedCount === 0 ? (
            <span className="text-xs text-muted-foreground">{t("syncNothingFollowed")}</span>
          ) : null}
        </div>
      ) : null}

      {syncFailure ? <p className="text-sm text-destructive">{`${t("syncFailed")} ${syncFailure.message}`}</p> : null}

      {accountsFailure ? <p className="text-sm text-destructive">{`${t("accountsSaveFailed")} ${accountsFailure.message}`}</p> : null}

      {connection.accounts.length > 0 ? (
        <div className="flex flex-col gap-3">
          {fopAccounts.length > 0 ? (
            <div className="flex flex-col gap-2">
              <h3 className="text-sm font-medium">{t("fopAccounts")}</h3>
              {fopAccounts.map((account) => (
                <div key={account.externalId} className="flex min-w-0 flex-col gap-0.5">
                  <CheckboxField
                    id={`monobank-account-${account.externalId}`}
                    label={`${account.currency} · ${account.maskedIban}`}
                    checked={account.isFollowed}
                    disabled={saveAccounts.isPending}
                    onChange={(checked) => toggleFollowed(account.externalId, checked)}
                  />
                  <SyncStatus account={account} />
                </div>
              ))}
            </div>
          ) : null}

          {unsupportedAccounts.length > 0 ? (
            <div className="flex flex-col gap-1">
              <h3 className="text-sm font-medium text-muted-foreground">{t("unsupportedAccounts")}</h3>
              <ul className="flex flex-col gap-1 text-sm text-muted-foreground">
                {unsupportedAccounts.map((account) => (
                  <li key={account.externalId}>
                    {account.currency} · {account.maskedIban} — {t("notSupported")}
                  </li>
                ))}
              </ul>
            </div>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

function SyncStatus({ account }: { account: MonobankAccountResponse }) {
  const t = useTranslations("settings.monobank");
  const locale = useLocale();

  const text = account.syncPending
    ? t("syncQueued")
    : account.lastSync
      ? t("lastSync", {
          at: formatInstantInKyiv(account.lastSync.at, locale),
          imported: account.lastSync.importedCount,
          skipped: account.lastSync.skippedCount,
        })
      : account.isFollowed
        ? t("neverSynced")
        : null;

  return text ? <p className="min-w-0 break-words pl-6 text-xs text-muted-foreground">{text}</p> : null;
}
