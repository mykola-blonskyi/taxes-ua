"use client";

import { useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import { useApiErrorText } from "@/data/api/useApiErrorText";
import { NotJsonError, TooLargeError } from "@/data/backup/backup";

const MAX_SHOWN_ERRORS = 10;

type Props = {
  error: unknown;
  notJson: string;
  tooLarge: string;
  failed: string;
};

export function UploadFailure({ error, notJson, tooLarge, failed }: Props) {
  const t = useTranslations("backup");
  const apiText = useApiErrorText();

  if (!error) {
    return null;
  }

  const apiFailure = error instanceof ApiError ? error : null;
  const missingInvoices = apiFailure?.extensions.missingInvoices;

  if (Array.isArray(missingInvoices)) {
    return (
      <p role="alert" className="min-w-0 break-words text-xs font-medium text-destructive">
        {t("missingInvoices", { numbers: missingInvoices.join(", ") })}
      </p>
    );
  }

  const errorEntries = Object.entries(apiFailure?.fieldCodes ?? {}).flatMap(([key, codes]) =>
    codes.map((code) => `${key}: ${apiText.ofCode(code)}`),
  );
  const shownErrors = errorEntries.slice(0, MAX_SHOWN_ERRORS);
  const moreErrors = errorEntries.length - shownErrors.length;
  const message =
    error instanceof NotJsonError
      ? notJson
      : error instanceof TooLargeError || apiFailure?.status === 413
        ? tooLarge
        : failed;
  const detail = apiFailure?.code && apiFailure.status !== 413 ? apiText.ofCode(apiFailure.code) : null;

  return (
    <div role="alert" className="flex min-w-0 flex-col gap-1 text-xs text-destructive">
      <p className="min-w-0 break-words font-medium">{message}</p>
      {detail ? <p className="min-w-0 break-words">{detail}</p> : null}
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
  );
}
