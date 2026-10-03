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
import { formatDateOnly, formatInstantInKyiv } from "@/shared/lib/dates";
import { Button } from "@/shared/ui/button";
import { SelectField } from "@/shared/ui/fields";
import type { Period } from "../period";
import { ProvisionalNote } from "./ProvisionalNote";

const declarationTypes = ["Reporting", "NewReporting", "Clarifying"] as const satisfies readonly DeclarationType[];

// Safari can drop a second download started in the same tick, so the annex follows a moment later.
const ANNEX_DOWNLOAD_DELAY_MS = 500;

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
  const tProvisional = useTranslations("declaration.provisional");
  const locale = useLocale();
  const generate = useGenerateDeclarationFile(period.year, period.quarter);
  const [chosen, setChosen] = useState<DeclarationType | null>(null);

  const type = chosen ?? declaration.filed?.type ?? "Reporting";
  const canPrepare = declaration.fileAvailable && declaration.readiness.ready && declaration.figures !== null;
  const failure = generate.error instanceof ApiError ? generate.error : null;

  function prepare() {
    generate.mutate(type, {
      onSuccess: (file) => {
        if (file) {
          startDownload(declarationFileUrl(period.year, period.quarter, file.type), file.fileName);
          if (file.annexFileName) {
            const annexUrl = declarationAnnexUrl(period.year, period.quarter, file.type);
            const annexFileName = file.annexFileName;
            window.setTimeout(() => startDownload(annexUrl, annexFileName), ANNEX_DOWNLOAD_DELAY_MS);
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
      {declaration.readiness.group3Confirmed ? null : <ProvisionalNote text={tProvisional("file")} />}

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
      {declaration.fileAvailable ? (
        canPrepare ? null : <p className="text-xs text-muted-foreground">{t("notReady")}</p>
      ) : (
        <p className="text-xs text-muted-foreground">
          {t("notEnded", { date: formatDateOnly(declaration.fileAvailableFrom, locale) })}
        </p>
      )}

      {generate.isError ? (
        <div className="flex min-w-0 flex-col gap-2 text-sm text-destructive" role="alert">
          <p>{failure?.status === 409 ? t("conflict") : failure?.status === 422 ? t("invalid") : t("failed")}</p>
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
      className="inline-flex w-fit items-center gap-1 font-medium text-primary underline-offset-4 hover:underline pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center"
    >
      <Download className="size-4" aria-hidden="true" />
      {children}
    </a>
  );
}
