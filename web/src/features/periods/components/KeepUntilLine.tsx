"use client";

import { useLocale, useTranslations } from "next-intl";
import { useKeepUntil } from "@/data/declarations/useKeepUntil";
import { formatNumericDate } from "@/shared/lib/dates";

// A failed load or a year without a group 3 declaration shows nothing: the line is an aid, and the screen
// around it must not depend on it.
export function KeepUntilLine({ year }: { year: number }) {
  const t = useTranslations("periods.keepUntil");
  const locale = useLocale();
  const { data } = useKeepUntil(year);
  const keepUntil = data?.keepUntil;

  if (!keepUntil) {
    return null;
  }

  const date = formatNumericDate(keepUntil.date, locale);
  const extended = keepUntil.state === "ExtendedWhileSuspended";

  return (
    <div className="flex min-w-0 flex-col gap-0.5">
      <p className="break-words text-sm">
        {extended ? t("extended", { year, date, days: keepUntil.daysAfterSuspension ?? 0 }) : t("fixed", { year, date })}
      </p>
      <p className="text-xs text-muted-foreground">{t("hint")}</p>
    </div>
  );
}
