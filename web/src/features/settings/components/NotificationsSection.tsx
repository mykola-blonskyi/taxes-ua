"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import {
  useConnectTelegram,
  useDisconnectTelegram,
  useNotificationChannels,
  useTestTelegram,
  useToggleTelegram,
  type NotificationChannel,
  type TelegramConnect,
} from "@/data/notifications/useNotificationChannels";
import { formatInstantInKyiv } from "@/shared/lib/dates";
import { Button } from "@/shared/ui/button";
import { CheckboxField } from "@/shared/ui/fields";
import { ChannelFailureNotice, ChannelStatusBadge } from "./ChannelParts";
import { EmailChannel } from "./EmailChannel";

export function NotificationsSection({
  confirmEmailToken,
  onEmailTokenSpent,
}: {
  confirmEmailToken?: string;
  onEmailTokenSpent: () => void;
}) {
  const t = useTranslations("settings.notifications");
  const [link, setLink] = useState<TelegramConnect | null>(null);
  const { data, isLoading, isError } = useNotificationChannels({ awaitingLink: link !== null });
  const telegram = data?.find((channel) => channel.kind === "Telegram");
  const email = data?.find((channel) => channel.kind === "Email");

  if (isLoading) {
    return <p className="text-sm text-muted-foreground">{t("loading")}</p>;
  }

  if (isError || !telegram || !email) {
    return <p className="text-sm text-destructive">{t("loadFailed")}</p>;
  }

  return (
    <div className="flex max-w-xl flex-col gap-8">
      <div className="flex flex-col gap-3">
        <h3 className="text-sm font-medium">{t("telegram.title")}</h3>
        <TelegramChannel channel={telegram} link={link} onLink={setLink} />
      </div>
      <div className="flex flex-col gap-3">
        <h3 className="text-sm font-medium">{t("email.title")}</h3>
        <EmailChannel channel={email} confirmToken={confirmEmailToken} onTokenSpent={onEmailTokenSpent} />
      </div>
    </div>
  );
}

function TelegramChannel({
  channel,
  link,
  onLink,
}: {
  channel: NotificationChannel;
  link: TelegramConnect | null;
  onLink: (link: TelegramConnect | null) => void;
}) {
  const t = useTranslations("settings.notifications.telegram");
  const locale = useLocale();
  const connect = useConnectTelegram();
  const toggle = useToggleTelegram();
  const test = useTestTelegram();
  const disconnect = useDisconnectTelegram();
  const [tested, setTested] = useState(false);

  if (!channel.available) {
    return <p className="text-sm text-muted-foreground">{t("unavailable")}</p>;
  }

  if (!channel.linked) {
    return (
      <div className="flex flex-col gap-3">
        <p className="text-sm text-muted-foreground">{t("intro")}</p>
        <div>
          <Button
            type="button"
            disabled={connect.isPending}
            onClick={() => connect.mutate(undefined, { onSuccess: (data) => onLink(data ?? null) })}
          >
            {connect.isPending ? t("connecting") : link ? t("newLink") : t("connect")}
          </Button>
        </div>
        {connect.isError ? <p className="text-sm text-destructive">{t("connectFailed")}</p> : null}
        {link ? (
          <div className="flex min-w-0 flex-col gap-2 rounded-lg border p-3">
            <p className="text-sm">{t("linkSteps")}</p>
            <a
              href={link.url}
              target="_blank"
              rel="noopener noreferrer"
              className="min-w-0 break-all text-sm text-primary underline-offset-4 hover:underline"
            >
              {t("openTelegram")}
            </a>
            <p className="text-xs text-muted-foreground">{t("linkExpires", { at: formatInstantInKyiv(link.expiresAt, locale) })}</p>
            <p role="status" className="text-xs text-muted-foreground">
              {t("waiting")}
            </p>
          </div>
        ) : null}
      </div>
    );
  }

  const changeFailed = toggle.isError || disconnect.isError;

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <ChannelStatusBadge on={channel.enabled}>{channel.enabled ? t("statusOn") : t("statusOff")}</ChannelStatusBadge>
        {channel.linkedAt ? (
          <span className="text-xs text-muted-foreground">{t("linkedAt", { at: formatInstantInKyiv(channel.linkedAt, locale) })}</span>
        ) : null}
      </div>

      <CheckboxField
        id="telegram-enabled"
        label={t("enabled")}
        checked={channel.enabled}
        disabled={toggle.isPending}
        onChange={(enabled) => toggle.mutate(enabled)}
      />

      {channel.lastFailure && channel.lastFailureAt ? (
        <ChannelFailureNotice>
          {t("lastFailure", { at: formatInstantInKyiv(channel.lastFailureAt, locale), reason: t(`failure.${channel.lastFailure}`) })}
          {channel.lastFailure === "Blocked" ? ` ${t("blockedHint")}` : ""}
        </ChannelFailureNotice>
      ) : null}
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
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={disconnect.isPending}
          onClick={() => disconnect.mutate(undefined, { onSuccess: () => onLink(null) })}
        >
          {disconnect.isPending ? t("disconnecting") : t("disconnect")}
        </Button>
      </div>
      {tested ? <p role="status" className="text-sm text-emerald-600 dark:text-emerald-400">{t("testSent")}</p> : null}
      {test.isError ? <p className="text-sm text-destructive">{t("testFailed")}</p> : null}
      {changeFailed ? <p className="text-sm text-destructive">{t("saveFailed")}</p> : null}
    </div>
  );
}
