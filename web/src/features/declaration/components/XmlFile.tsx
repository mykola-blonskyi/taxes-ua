"use client";

import { useState, type ReactNode } from "react";
import { Download } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import {
  declarationAnnexUrl,
  declarationFileUrl,
  useGenerateDeclarationFile,
  type DeclarationFile,
  type DeclarationResponse,
  type DeclarationType,
} from "@/data/declarations/useDeclarations";
import { formatInstantInKyiv } from "@/shared/lib/dates";
import { Button } from "@/shared/ui/button";
import { SelectField } from "@/shared/ui/fields";
import type { Period } from "../period";

const declarationTypes = ["Reporting", "NewReporting", "Clarifying"] as const satisfies readonly DeclarationType[];

function startDownload(url: string, fileName: string) {
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
}

export function XmlFile({ declaration, period }: { declaration: DeclarationResponse; period: Period }) {
  const t = useTranslations("declaration.xml");
  const tTypes = useTranslations("declaration.types");
  const locale = useLocale();
  const generate = useGenerateDeclarationFile(period.year, period.quarter);
  const [chosen, setChosen] = useState<DeclarationType | null>(null);

  const type = chosen ?? declaration.filed?.type ?? "Reporting";
  const canPrepare = declaration.readiness.ready && declaration.figures !== null;
  const failure = generate.error instanceof ApiError ? generate.error : null;
  const fileErrors = failure?.status === 422 ? (failure.errors.file ?? []) : [];

  function prepare() {
    generate.mutate(type, {
      onSuccess: (file) => {
        if (file) {
          startDownload(declarationFileUrl(period.year, period.quarter, file.type), file.fileName);
          if (file.annexFileName) {
            startDownload(declarationAnnexUrl(period.year, period.quarter, file.type), file.annexFileName);
          }
        }
      },
    });
  }

  return (
    <section className="flex min-w-0 flex-col gap-3" aria-labelledby="xml-heading">
      <div className="flex flex-col gap-1">
        <h3 id="xml-heading" className="text-base font-semibold">
          {t("title")}
        </h3>
        <p className="text-xs text-muted-foreground">{t("hint")}</p>
        {declaration.figures?.esvKop != null ? (
          <p className="text-xs text-muted-foreground">{t("annexHint")}</p>
        ) : null}
      </div>

      <div className="flex min-w-0 flex-col gap-3 sm:flex-row sm:items-end">
        <div className="min-w-0 sm:w-64">
          <SelectField
            id="xml-type"
            label={t("type")}
            value={type}
            onChange={(value) => setChosen(value as DeclarationType)}
            options={declarationTypes.map((value) => ({ value, label: tTypes(value) }))}
          />
        </div>
        <div>
          <Button type="button" disabled={!canPrepare || generate.isPending} onClick={prepare}>
            <Download aria-hidden="true" />
            {generate.isPending ? t("preparing") : t("download")}
          </Button>
        </div>
      </div>
      {canPrepare ? null : <p className="text-xs text-muted-foreground">{t("notReady")}</p>}

      {generate.isError ? (
        <div className="flex min-w-0 flex-col gap-2 text-sm text-destructive" role="alert">
          <p>{failure?.status === 409 ? t("conflict") : failure?.status === 422 ? t("invalid") : t("failed")}</p>
          {fileErrors.length > 0 ? (
            <ul className="list-disc break-words pl-5 text-xs text-muted-foreground">
              {fileErrors.map((message) => (
                <li key={message}>{message}</li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}

      <div className="flex min-w-0 flex-col gap-2">
        <h4 className="text-sm font-medium">{t("files")}</h4>
        {declaration.files.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("noFiles")}</p>
        ) : (
          <ul className="flex min-w-0 flex-col gap-2">
            {declaration.files.map((file) => (
              <FileRow key={file.type} file={file} period={period} locale={locale} />
            ))}
          </ul>
        )}
      </div>

      <div className="flex min-w-0 flex-col gap-2">
        <h4 className="text-sm font-medium">{t("guide.title")}</h4>
        <ol className="flex list-decimal flex-col gap-1 pl-5 text-sm">
          <li>{t("guide.open")}</li>
          <li>{t("guide.import")}</li>
          <li>{t("guide.check")}</li>
          <li>{t("guide.sign")}</li>
        </ol>
        <p className="text-xs text-muted-foreground">{t("guide.noSend")}</p>
      </div>
    </section>
  );
}

function FileRow({ file, period, locale }: { file: DeclarationFile; period: Period; locale: string }) {
  const t = useTranslations("declaration.xml");
  const tTypes = useTranslations("declaration.types");

  return (
    <li className="flex min-w-0 flex-col gap-1 rounded-lg border bg-card p-3 text-sm">
      <span className="font-medium">{tTypes(file.type)}</span>
      <span className="break-all text-xs">{file.fileName}</span>
      <span className="text-xs text-muted-foreground">{formatInstantInKyiv(file.generatedAt, locale)}</span>
      <DownloadLink href={declarationFileUrl(period.year, period.quarter, file.type)} fileName={file.fileName}>
        {file.annexFileName ? t("downloadDeclaration") : t("downloadAgain")}
      </DownloadLink>
      {file.annexFileName ? (
        <>
          <span className="break-all text-xs">{file.annexFileName}</span>
          <DownloadLink href={declarationAnnexUrl(period.year, period.quarter, file.type)} fileName={file.annexFileName}>
            {t("downloadAnnex")}
          </DownloadLink>
        </>
      ) : null}
    </li>
  );
}

function DownloadLink({ href, fileName, children }: { href: string; fileName: string; children: ReactNode }) {
  return (
    <a
      href={href}
      download={fileName}
      className="inline-flex w-fit items-center gap-1 font-medium text-primary underline-offset-4 hover:underline"
    >
      <Download className="size-4" aria-hidden="true" />
      {children}
    </a>
  );
}
