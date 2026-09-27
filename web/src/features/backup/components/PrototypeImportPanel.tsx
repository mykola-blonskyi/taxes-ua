"use client";

import { useRef, useState, type ChangeEvent } from "react";
import { useTranslations } from "next-intl";
import { useImportPrototype, type ImportResponse } from "@/data/backup/backup";
import { Button } from "@/shared/ui/button";
import { UploadFailure } from "./UploadFailure";

function Counts({ result }: { result: ImportResponse }) {
  const t = useTranslations("prototypeImport");

  return (
    <ul className="flex min-w-0 flex-col gap-0.5 text-sm">
      <li>{t("counts.transactions", { count: Number(result.transactionsAdded) })}</li>
      <li>{t("counts.payments", { count: Number(result.paymentsAdded) })}</li>
      <li className="text-muted-foreground">
        {t("counts.alreadyPresent", {
          transactions: Number(result.transactionsAlreadyPresent),
          payments: Number(result.paymentsAlreadyPresent),
        })}
      </li>
      {Number(result.paymentsNothingDue) > 0 ? (
        <li className="text-muted-foreground">
          {t("counts.nothingDue", { count: Number(result.paymentsNothingDue) })}
        </li>
      ) : null}
    </ul>
  );
}

export function PrototypeImportPanel() {
  const t = useTranslations("prototypeImport");
  const preview = useImportPrototype();
  const importFile = useImportPrototype();
  const inputRef = useRef<HTMLInputElement>(null);
  const [pendingFile, setPendingFile] = useState<File | null>(null);

  function reset() {
    setPendingFile(null);
    preview.reset();
    if (inputRef.current) {
      inputRef.current.value = "";
    }
  }

  function handleFileChange(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0] ?? null;

    importFile.reset();
    preview.reset();
    setPendingFile(file);
    if (file) {
      preview.mutate({ file, dryRun: true });
    }
  }

  function handleConfirm() {
    if (!pendingFile) {
      return;
    }

    importFile.mutate({ file: pendingFile, dryRun: false }, { onSuccess: reset });
  }

  const nothingToAdd =
    preview.data && Number(preview.data.transactionsAdded) + Number(preview.data.paymentsAdded) === 0;

  return (
    <section className="flex flex-col gap-3">
      <h3 className="text-base font-semibold">{t("title")}</h3>
      <p className="text-sm text-muted-foreground">{t("description")}</p>

      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={preview.isPending || importFile.isPending}
          onClick={() => inputRef.current?.click()}
        >
          {preview.isPending ? t("checking") : t("choose")}
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

      {pendingFile && preview.data ? (
        <div className="flex min-w-0 flex-col gap-2 rounded-lg border p-3">
          <p className="min-w-0 break-all text-xs text-muted-foreground">
            {t("confirm.message", { fileName: pendingFile.name })}
          </p>
          <Counts result={preview.data} />
          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              size="sm"
              disabled={importFile.isPending || nothingToAdd}
              onClick={handleConfirm}
            >
              {importFile.isPending ? t("confirm.importing") : t("confirm.yes")}
            </Button>
            <Button type="button" variant="outline" size="sm" disabled={importFile.isPending} onClick={reset}>
              {t("confirm.cancel")}
            </Button>
          </div>
        </div>
      ) : null}

      {importFile.isSuccess && importFile.data ? (
        <div role="status" className="flex min-w-0 flex-col gap-1">
          <p className="text-sm font-medium">{t("success")}</p>
          <Counts result={importFile.data} />
        </div>
      ) : null}

      <UploadFailure
        error={preview.error ?? importFile.error}
        notJson={t("notJson")}
        tooLarge={t("tooLarge")}
        failed={t("failed")}
      />
    </section>
  );
}
