"use client";

import { useRef, useState, type ChangeEvent } from "react";
import { useTranslations } from "next-intl";
import { Download } from "lucide-react";
import { ApiError } from "@/data/api/client";
import { backupUrl, NotJsonError, useRestoreBackup } from "@/data/backup/backup";
import { Button } from "@/shared/ui/button";

const MAX_SHOWN_ERRORS = 10;

export function BackupPanel() {
  const t = useTranslations("backup");
  const restoreBackup = useRestoreBackup();
  const inputRef = useRef<HTMLInputElement>(null);
  const [pendingFile, setPendingFile] = useState<File | null>(null);

  const notJson = restoreBackup.error instanceof NotJsonError;
  const apiFailure = restoreBackup.error instanceof ApiError ? restoreBackup.error : null;
  const errorEntries = Object.entries(apiFailure?.errors ?? {}).flatMap(([key, messages]) =>
    messages.map((message) => `${key}: ${message}`),
  );
  const shownErrors = errorEntries.slice(0, MAX_SHOWN_ERRORS);
  const moreErrors = errorEntries.length - shownErrors.length;
  const failureMessage = notJson
    ? t("notJson")
    : apiFailure?.status === 413
      ? t("tooLarge")
      : restoreBackup.isError
        ? t("failed")
        : null;
  const failureDetail = apiFailure && apiFailure.status !== 413 ? apiFailure.message : null;

  function resetInput() {
    if (inputRef.current) {
      inputRef.current.value = "";
    }
  }

  function handleFileChange(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0] ?? null;

    restoreBackup.reset();
    setPendingFile(file);
  }

  function handleCancel() {
    setPendingFile(null);
    resetInput();
  }

  function handleConfirm() {
    if (!pendingFile) {
      return;
    }

    restoreBackup.mutate(pendingFile, {
      onSuccess: () => {
        setPendingFile(null);
        resetInput();
      },
    });
  }

  return (
    <section className="flex flex-col gap-3">
      <h3 className="text-base font-semibold">{t("title")}</h3>
      <p className="text-sm text-muted-foreground">{t("description")}</p>

      <div className="flex flex-wrap items-center gap-2">
        <Button asChild variant="outline" size="sm">
          <a href={backupUrl} download>
            <Download aria-hidden="true" />
            {t("download")}
          </a>
        </Button>

        <Button type="button" variant="outline" size="sm" onClick={() => inputRef.current?.click()}>
          {t("restore")}
        </Button>
        <input
          ref={inputRef}
          type="file"
          accept="application/json,.json"
          className="sr-only"
          tabIndex={-1}
          aria-hidden="true"
          onChange={handleFileChange}
        />
      </div>

      {pendingFile ? (
        <div className="flex min-w-0 flex-col gap-2 rounded-lg border border-destructive/40 p-3">
          <p className="min-w-0 break-all text-xs text-destructive">
            {t("confirm.message", { fileName: pendingFile.name })}
          </p>
          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              variant="destructive"
              size="sm"
              disabled={restoreBackup.isPending}
              onClick={handleConfirm}
            >
              {restoreBackup.isPending ? t("confirm.restoring") : t("confirm.yes")}
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={restoreBackup.isPending}
              onClick={handleCancel}
            >
              {t("confirm.cancel")}
            </Button>
          </div>
        </div>
      ) : null}

      {restoreBackup.isSuccess && restoreBackup.data ? (
        <p role="status" className="text-sm text-muted-foreground">
          {t("success", {
            clients: Number(restoreBackup.data.clients),
            transactions: Number(restoreBackup.data.transactions),
            budgetPayments: Number(restoreBackup.data.budgetPayments),
          })}
        </p>
      ) : null}

      {failureMessage ? (
        <div role="alert" className="flex min-w-0 flex-col gap-1 text-xs text-destructive">
          <p className="min-w-0 break-words font-medium">{failureMessage}</p>
          {failureDetail ? <p className="min-w-0 break-words">{failureDetail}</p> : null}
          {shownErrors.length > 0 ? (
            <ul className="flex min-w-0 flex-col gap-0.5">
              {shownErrors.map((entry) => (
                <li key={entry} className="min-w-0 break-all">
                  {entry}
                </li>
              ))}
            </ul>
          ) : null}
          {moreErrors > 0 ? <p>{t("errorsMore", { count: moreErrors })}</p> : null}
        </div>
      ) : null}
    </section>
  );
}
