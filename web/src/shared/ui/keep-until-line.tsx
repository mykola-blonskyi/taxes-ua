import { useLocale, useTranslations } from "next-intl";
import { formatNumericDate } from "@/shared/lib/dates";

type KeepUntil = {
  state: "Fixed" | "ExtendedWhileSuspended";
  date: string;
  daysAfterSuspension: null | number | string;
};

// A year without a group 3 declaration, or a date that failed to load, shows nothing: the line is an aid, and
// the screen around it must not depend on it.
export function KeepUntilLine({ year, keepUntil }: { year: number; keepUntil: KeepUntil | null | undefined }) {
  const t = useTranslations("periods.keepUntil");
  const locale = useLocale();

  if (!keepUntil) {
    return null;
  }

  const date = formatNumericDate(keepUntil.date, locale);
  const extended = keepUntil.state === "ExtendedWhileSuspended";

  return (
    <div className="flex min-w-0 flex-col gap-0.5">
      <p className="break-words text-sm">
        {extended
          ? t("extended", { year, date, days: Number(keepUntil.daysAfterSuspension ?? 0) })
          : t("fixed", { year, date })}
      </p>
      <p className="text-xs text-muted-foreground">{t("hint")}</p>
    </div>
  );
}
