"use client";

import { useState, type ReactNode } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { formatPlainAmount, parseHryvnia } from "@/shared/lib/money";
import { CopyField } from "@/shared/ui/copy-field";
import { TextField } from "@/shared/ui/fields";
import { Sheet } from "@/shared/ui/sheet";

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
    </div>
  );
}
