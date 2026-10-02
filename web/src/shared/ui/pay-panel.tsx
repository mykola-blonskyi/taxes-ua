"use client";

import { useState, type ReactNode } from "react";
import Link from "next/link";
import { Maximize2Icon, XIcon } from "lucide-react";
import { Dialog } from "radix-ui";
import { useTranslations } from "next-intl";
import { formatPlainAmount, parseHryvnia } from "@/shared/lib/money";
import { Button } from "@/shared/ui/button";
import { CopyField } from "@/shared/ui/copy-field";
import { TextField } from "@/shared/ui/fields";
import { NbuQrCode, encodeNbuQr } from "@/shared/ui/nbu-qr";
import { Sheet } from "@/shared/ui/sheet";

// Every recipient here is a Treasury account. False hides the QR behind a note, for when a real scan
// shows banks refuse an NBU QR for a budget transfer (#100).
const qrForTreasuryAccounts = true;

const missingLabels = {
  iban: "missing.iban",
  recipientName: "missing.recipientName",
  recipientCode: "missing.recipientCode",
} as const;

type MissingField = keyof typeof missingLabels;

type PayDetails = {
  purpose: string;
  recipient: { iban: string; name: string; code: string } | null;
  missing: string[];
  qrContent: string | null;
  // The request for the amount's QR has not answered yet, so a null qrContent does not mean "cannot be encoded".
  qrPending: boolean;
  amountKop: number | string | null;
};

export function PayPanel({
  open,
  onOpenChange,
  title,
  initialAmountKop,
  onAmountChange,
  details,
  loading,
  failed,
  notComputed,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  initialAmountKop: number | null;
  onAmountChange: (amountKop: number | null) => void;
  details: PayDetails | undefined;
  loading: boolean;
  failed: boolean;
  notComputed: boolean;
}) {
  const t = useTranslations("pay");

  return (
    <Sheet
      open={open}
      onOpenChange={onOpenChange}
      title={title}
      description={t("statement")}
      closeLabel={t("close")}
    >
      <PayBody
        initialAmountKop={initialAmountKop}
        onAmountChange={onAmountChange}
        details={details}
        loading={loading}
        failed={failed}
        notComputed={notComputed}
      />
    </Sheet>
  );
}

function PayBody({
  initialAmountKop,
  onAmountChange,
  details,
  loading,
  failed,
  notComputed,
}: {
  initialAmountKop: number | null;
  onAmountChange: (amountKop: number | null) => void;
  details: PayDetails | undefined;
  loading: boolean;
  failed: boolean;
  notComputed: boolean;
}) {
  const t = useTranslations("pay");
  const [amountText, setAmountText] = useState(
    initialAmountKop !== null && initialAmountKop > 0 ? formatPlainAmount(initialAmountKop) : "",
  );
  const parsed = parseHryvnia(amountText);
  const amountKop = parsed !== null && parsed > 0 ? parsed : null;
  const copyProps = (field: string) => ({
    label: field,
    copyLabel: t("copy", { field }),
    copiedLabel: t("copied"),
    failedLabel: t("copyFailed"),
  });

  return (
    <div className="flex min-w-0 flex-col gap-4">
      <TextField
        id="pay-amount"
        label={t("amountInput")}
        value={amountText}
        inputMode="decimal"
        autoComplete="off"
        errors={amountKop === null ? [t("amountInvalid")] : undefined}
        onChange={(text) => {
          setAmountText(text);
          const next = parseHryvnia(text);
          onAmountChange(next !== null && next > 0 ? next : null);
        }}
      />

      {notComputed ? (
        <p className="text-sm text-muted-foreground">{t("notComputed")}</p>
      ) : failed ? (
        <p className="text-sm text-destructive">{t("failed")}</p>
      ) : details ? (
        <Details details={details} amountKop={amountKop} copyProps={copyProps} />
      ) : loading ? (
        <p className="text-sm text-muted-foreground">{t("loading")}</p>
      ) : null}
    </div>
  );
}

function Details({
  details,
  amountKop,
  copyProps,
}: {
  details: PayDetails;
  amountKop: number | null;
  copyProps: (field: string) => { label: string; copyLabel: string; copiedLabel: string; failedLabel: string };
}) {
  const t = useTranslations("pay");
  const { recipient, missing } = details;

  if (missing.length > 0 || recipient === null) {
    return (
      <div className="flex flex-col gap-2 rounded-lg border bg-muted p-3">
        <p className="text-sm">{t("missingIntro")}</p>
        <ul className="list-disc pl-5 text-sm">
          {missing.map((field) => (
            <li key={field}>{field in missingLabels ? t(missingLabels[field as MissingField]) : field}</li>
          ))}
        </ul>
        <Link
          href="/settings?tab=treasury"
          className="text-sm font-medium text-primary underline-offset-4 hover:underline"
        >
          {t("settingsLink")}
        </Link>
      </div>
    );
  }

  return (
    <div className="flex min-w-0 flex-col gap-3">
      <CopyField {...copyProps(t("recipientName"))} value={recipient.name} />
      <CopyField {...copyProps(t("recipientCode"))} value={recipient.code} />
      <CopyField {...copyProps(t("iban"))} value={recipient.iban} />
      <CopyField
        {...copyProps(t("amount"))}
        value={amountKop === null ? "—" : formatPlainAmount(amountKop)}
        copyValue={amountKop === null ? null : formatPlainAmount(amountKop)}
      />
      <CopyField {...copyProps(t("purpose"))} value={details.purpose} />
      <QrBlock details={details} amountKop={amountKop} />
    </div>
  );
}

function QrBlock({ details, amountKop }: { details: PayDetails; amountKop: number | null }) {
  const t = useTranslations("pay");
  const [enlarged, setEnlarged] = useState(false);

  if (!qrForTreasuryAccounts) {
    return <p className="text-sm text-muted-foreground">{t("qrTreasuryOff")}</p>;
  }
  // Without an amount nothing was asked for, so a null qrContent is not a failure to encode.
  if (amountKop === null || (details.qrContent === null && details.qrPending)) {
    return <p className="text-sm text-muted-foreground">{t("qrUpdating")}</p>;
  }
  if (details.qrContent === null || encodeNbuQr(details.qrContent) === null) {
    return <p className="text-sm text-muted-foreground">{t("qrUnavailable")}</p>;
  }
  // keepPreviousData keeps the old amount's details on screen while the new ones load.
  if (details.amountKop === null || Number(details.amountKop) !== amountKop) {
    return <p className="text-sm text-muted-foreground">{t("qrUpdating")}</p>;
  }

  return (
    <div className="flex min-w-0 flex-col items-center gap-2">
      <div className="w-full max-w-[240px]">
        <NbuQrCode content={details.qrContent} label={t("qrLabel")} />
      </div>
      <p className="text-center text-sm text-muted-foreground">{t("qrHint")}</p>
      <Dialog.Root open={enlarged} onOpenChange={setEnlarged}>
        <Dialog.Trigger asChild>
          <Button type="button" variant="outline" size="sm">
            <Maximize2Icon />
            {t("qrEnlarge")}
          </Button>
        </Dialog.Trigger>
        <Dialog.Portal>
          <Dialog.Overlay className="fixed inset-0 z-[60] bg-black/60" />
          <Dialog.Content
            aria-describedby={undefined}
            className="fixed left-1/2 top-1/2 z-[60] w-[min(90vw,90vh)] -translate-x-1/2 -translate-y-1/2 rounded-xl bg-white p-2 outline-none"
          >
            <Dialog.Title className="sr-only">{t("qrLabel")}</Dialog.Title>
            <Dialog.Close
              aria-label={t("close")}
              className="absolute -right-2 -top-2 rounded-full border bg-background p-1.5 text-foreground shadow outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
            >
              <XIcon className="size-5" />
            </Dialog.Close>
            <NbuQrCode content={details.qrContent} label={t("qrLabel")} />
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>
    </div>
  );
}
