"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { LoadState } from "@/data/api/LoadState";
import { Download } from "lucide-react";
import { calendarDownloadUrl, useCalendarFeed, useRotateCalendarFeed } from "@/data/calendar/useCalendarFeed";
import { Button } from "@/shared/ui/button";
import { CopyField } from "@/shared/ui/copy-field";

export function CalendarFeedSection() {
  const t = useTranslations("settings.calendar");
  const query = useCalendarFeed();
  const { data } = query;
  const rotate = useRotateCalendarFeed();
  const [confirming, setConfirming] = useState(false);
  const path = data?.path ?? null;
  const url = path ? `${window.location.origin}${path}` : null;

  return (
    <div className="flex max-w-xl flex-col gap-3">
      <h3 className="text-sm font-medium">{t("title")}</h3>
      <p className="text-sm text-muted-foreground">{t("intro")}</p>

      {query.isLoading || query.isError ? (
        <LoadState query={query} loading={t("loading")} failed={t("loadFailed")} />
      ) : null}

      {!query.isLoading && !query.isError && !url ? (
        <div className="flex flex-col gap-2">
          <p className="text-sm text-muted-foreground">{t("none")}</p>
          <div>
            <Button type="button" disabled={rotate.isPending} onClick={() => rotate.mutate()}>
              {rotate.isPending ? t("creating") : t("create")}
            </Button>
          </div>
        </div>
      ) : null}

      {url ? (
        <div className="flex min-w-0 flex-col gap-3 rounded-lg border p-3">
          <CopyField
            label={t("urlLabel")}
            value={url}
            copyLabel={t("copy")}
            copiedLabel={t("copied")}
            failedLabel={t("copyFailed")}
          />
          <div className="flex flex-wrap items-center gap-2">
            <Button asChild size="sm">
              <a href={url.replace(/^https?:/, "webcal:")}>{t("openInCalendar")}</a>
            </Button>
            {confirming ? null : (
              <Button type="button" variant="outline" size="sm" onClick={() => setConfirming(true)}>
                {t("rotate")}
              </Button>
            )}
          </div>
          {confirming ? (
            <div role="alertdialog" aria-label={t("rotate")} className="flex flex-col gap-2 rounded-lg border border-destructive/40 bg-destructive/10 p-3">
              <p className="text-sm">{t("rotateWarning")}</p>
              <div className="flex flex-wrap items-center gap-2">
                <Button
                  type="button"
                  variant="destructive"
                  size="sm"
                  disabled={rotate.isPending}
                  onClick={() => rotate.mutate(undefined, { onSuccess: () => setConfirming(false) })}
                >
                  {rotate.isPending ? t("rotating") : t("rotateConfirm")}
                </Button>
                <Button type="button" variant="outline" size="sm" onClick={() => setConfirming(false)}>
                  {t("cancel")}
                </Button>
              </div>
            </div>
          ) : null}
        </div>
      ) : null}
      {rotate.isError ? <p className="text-sm text-destructive">{t("rotateFailed")}</p> : null}

      <div className="flex flex-wrap items-center gap-2">
        <Button asChild variant="outline" size="sm">
          <a href={calendarDownloadUrl} download>
            <Download aria-hidden="true" />
            {t("download")}
          </a>
        </Button>
      </div>
      <p className="text-xs text-muted-foreground">{t("privacy")}</p>
      <p className="text-xs text-muted-foreground">{t("backup")}</p>
    </div>
  );
}
