"use client";

import { useEffect, useRef, useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { problemOf } from "@/data/api/client";
import { useApiErrorText } from "@/data/api/useApiErrorText";
import {
  useAddEmail,
  useConfirmEmail,
  useRemoveEmail,
  useResendEmailConfirmation,
  useTestEmail,
  useToggleEmail,
  type NotificationChannel,
} from "@/data/notifications/useNotificationChannels";
import { formatInstantInKyiv } from "@/shared/lib/dates";
import { Button } from "@/shared/ui/button";
import { CheckboxField, TextField } from "@/shared/ui/fields";
import { ChannelFailureNotice, ChannelStatusBadge } from "./ChannelParts";

// The link in the confirmation email opens settings with the token in the address. It is spent once,
// as soon as this mounts: the parent forgets it (so coming back to the tab does not post it again),
// and it is taken out of the address bar so a reload or a shared screenshot does not carry it. The ref
// only stops a second effect run in development.
export function EmailChannel({
  channel,
  confirmToken,
  onTokenSpent,
}: {
  channel: NotificationChannel;
  confirmToken?: string;
  onTokenSpent: () => void;
}) {
  const t = useTranslations("settings.notifications.email");
  const apiText = useApiErrorText();
  const { mutate: confirmEmail, ...confirm } = useConfirmEmail();
  const spent = useRef(false);

  useEffect(() => {
    if (!confirmToken || spent.current) {
      return;
    }

    spent.current = true;
    onTokenSpent();
    confirmEmail(confirmToken);
    window.history.replaceState(null, "", "/settings?tab=notifications");
  }, [confirmToken, confirmEmail, onTokenSpent]);

  return (
    <div className="flex flex-col gap-3">
      {confirm.isPending ? <p className="text-sm text-muted-foreground">{t("confirming")}</p> : null}
      {confirm.isSuccess ? (
        <p role="status" className="text-sm text-emerald-600 dark:text-emerald-400">
          {t("confirmed", { address: confirm.data?.address ?? "" })}
        </p>
      ) : null}
      {confirm.error ? (
        <p role="alert" className="text-sm text-destructive">
          {problemOf(confirm.error)?.status === 400
            ? t("linkInvalid")
            : problemOf(confirm.error)?.status === 410
              ? t("linkExpired")
              : apiText.withReason(t("confirmFailed"), confirm.error)}
        </p>
      ) : null}
      {!channel.available ? (
        <p className="text-sm text-muted-foreground">{t("unavailable")}</p>
      ) : !channel.linked ? (
        <AddressForm />
      ) : channel.confirmed ? (
        <Confirmed channel={channel} />
      ) : (
        <Pending channel={channel} />
      )}
    </div>
  );
}

function AddressForm() {
  const t = useTranslations("settings.notifications.email");
  const apiText = useApiErrorText();
  const add = useAddEmail();
  const [address, setAddress] = useState("");
  const invalid = problemOf(add.error)?.status === 400;

  return (
    <form
      className="flex flex-col gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        add.mutate(address);
      }}
    >
      <p className="text-sm text-muted-foreground">{t("intro")}</p>
      <TextField
        id="email-address"
        label={t("addressLabel")}
        type="email"
        inputMode="email"
        autoComplete="email"
        value={address}
        onChange={setAddress}
        errors={invalid ? [t("invalidAddress")] : undefined}
      />
      <div>
        <Button type="submit" disabled={add.isPending || address.trim() === ""}>
          {add.isPending ? t("adding") : t("add")}
        </Button>
      </div>
      {add.isError && !invalid ? <p className="text-sm text-destructive">{apiText.withReason(t("addFailed"), add.error)}</p> : null}
    </form>
  );
}

function Pending({ channel }: { channel: NotificationChannel }) {
  const t = useTranslations("settings.notifications.email");
  const apiText = useApiErrorText();
  const locale = useLocale();
  const resend = useResendEmailConfirmation();
  const remove = useRemoveEmail();

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <ChannelStatusBadge on={false}>{t("statusPending")}</ChannelStatusBadge>
        <span className="min-w-0 break-all text-sm">{channel.address}</span>
      </div>
      <p className="text-sm text-muted-foreground">{t("pending")}</p>
      <FailureNotice channel={channel} locale={locale} />
      <div className="flex flex-wrap items-center gap-2">
        <Button type="button" size="sm" disabled={resend.isPending} onClick={() => resend.mutate()}>
          {resend.isPending ? t("resending") : t("resend")}
        </Button>
        <Button type="button" variant="outline" size="sm" disabled={remove.isPending} onClick={() => remove.mutate()}>
          {remove.isPending ? t("removing") : t("remove")}
        </Button>
      </div>
      {resend.isSuccess ? (
        <p role="status" className="text-sm text-emerald-600 dark:text-emerald-400">
          {t("resent")}
        </p>
      ) : null}
      {resend.isError ? <p className="text-sm text-destructive">{apiText.withReason(t("resendFailed"), resend.error)}</p> : null}
      {remove.isError ? <p className="text-sm text-destructive">{apiText.withReason(t("saveFailed"), remove.error)}</p> : null}
    </div>
  );
}

function Confirmed({ channel }: { channel: NotificationChannel }) {
  const t = useTranslations("settings.notifications.email");
  const apiText = useApiErrorText();
  const locale = useLocale();
  const toggle = useToggleEmail();
  const test = useTestEmail();
  const remove = useRemoveEmail();
  const [tested, setTested] = useState(false);

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <ChannelStatusBadge on={channel.enabled}>{channel.enabled ? t("statusOn") : t("statusOff")}</ChannelStatusBadge>
        <span className="min-w-0 break-all text-sm">{channel.address}</span>
      </div>

      <CheckboxField
        id="email-enabled"
        label={t("enabled")}
        checked={channel.enabled}
        disabled={toggle.isPending}
        onChange={(enabled) => toggle.mutate(enabled)}
      />

      <FailureNotice channel={channel} locale={locale} />
      {channel.lastDeliveryAt ? (
        <p className="text-xs text-muted-foreground">{t("lastDelivery", { at: formatInstantInKyiv(channel.lastDeliveryAt, locale) })}</p>
      ) : null}

      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          size="sm"
          disabled={test.isPending}
          onClick={() => {
            setTested(false);
            test.mutate(undefined, { onSuccess: () => setTested(true) });
          }}
        >
          {test.isPending ? t("testing") : t("test")}
        </Button>
        <Button type="button" variant="outline" size="sm" disabled={remove.isPending} onClick={() => remove.mutate()}>
          {remove.isPending ? t("removing") : t("remove")}
        </Button>
      </div>
      {tested ? (
        <p role="status" className="text-sm text-emerald-600 dark:text-emerald-400">
          {t("testSent")}
        </p>
      ) : null}
      {test.isError ? <p className="text-sm text-destructive">{apiText.withReason(t("testFailed"), test.error)}</p> : null}
      {toggle.isError || remove.isError ? (
        <p className="text-sm text-destructive">{apiText.withReason(t("saveFailed"), toggle.error ?? remove.error)}</p>
      ) : null}
    </div>
  );
}

function FailureNotice({ channel, locale }: { channel: NotificationChannel; locale: string }) {
  const t = useTranslations("settings.notifications.email");

  if (!channel.lastFailure || !channel.lastFailureAt) {
    return null;
  }

  return (
    <ChannelFailureNotice>
      {t("lastFailure", { at: formatInstantInKyiv(channel.lastFailureAt, locale), reason: t(`failure.${channel.lastFailure}`) })}
    </ChannelFailureNotice>
  );
}
