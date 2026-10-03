"use client";

import { useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { ApiError } from "@/data/api/client";
import { useApiErrorText } from "@/data/api/useApiErrorText";
import {
  maxSignatureBytes,
  signatureTypes,
  signatureUrl,
  useDeleteSignature,
  useUploadSignature,
  type InvoicingDetailsResponse,
} from "@/data/invoicing/useInvoicing";
import { Button } from "@/shared/ui/button";

export function InvoicingSignature({ details }: { details: InvoicingDetailsResponse }) {
  const t = useTranslations("settings.invoicing.signature");
  const apiText = useApiErrorText();
  const upload = useUploadSignature();
  const remove = useDeleteSignature();
  const input = useRef<HTMLInputElement>(null);
  const [rejection, setRejection] = useState<string | null>(null);

  const failure = upload.error instanceof ApiError ? upload.error : remove.error instanceof ApiError ? remove.error : null;
  const message =
    rejection ??
    (failure ? apiText.withReason(t("failed"), failure) : null);

  function choose(file: File | undefined) {
    setRejection(null);
    upload.reset();
    if (!file) {
      return;
    }

    if (!(signatureTypes as readonly string[]).includes(file.type)) {
      setRejection(t("wrongType"));
    } else if (file.size > maxSignatureBytes) {
      setRejection(t("tooLarge"));
    } else {
      upload.mutate(file);
    }

    if (input.current) {
      input.current.value = "";
    }
  }

  return (
    <section className="flex min-w-0 flex-col gap-3" aria-labelledby="signature-heading">
      <div className="flex flex-col gap-1">
        <h3 id="signature-heading" className="text-base font-semibold">
          {t("title")}
        </h3>
        <p className="text-xs text-muted-foreground">{t("hint", { size: maxSignatureBytes / 1024 })}</p>
      </div>
      {details.hasSignature ? (
        // eslint-disable-next-line @next/next/no-img-element -- an authorised, private endpoint: next/image would fetch it without the session
        <img
          src={`${signatureUrl}?v=${encodeURIComponent(details.signatureUpdatedAt ?? "")}`}
          alt={t("preview")}
          className="max-h-24 max-w-full self-start rounded-lg border bg-white p-2"
        />
      ) : (
        <p className="text-sm text-muted-foreground">{t("none")}</p>
      )}
      <div className="flex flex-wrap items-center gap-2">
        <input
          ref={input}
          id="signature-file"
          type="file"
          accept={signatureTypes.join(",")}
          className="sr-only"
          tabIndex={-1}
          aria-hidden="true"
          onChange={(event) => choose(event.target.files?.[0])}
        />
        <Button type="button" variant="outline" disabled={upload.isPending} onClick={() => input.current?.click()}>
          {upload.isPending ? t("uploading") : details.hasSignature ? t("replace") : t("upload")}
        </Button>
        {details.hasSignature ? (
          <Button type="button" variant="outline" disabled={remove.isPending} onClick={() => remove.mutate()}>
            {t("remove")}
          </Button>
        ) : null}
      </div>
      {message ? (
        <p role="alert" className="text-sm text-destructive">
          {message}
        </p>
      ) : null}
    </section>
  );
}
