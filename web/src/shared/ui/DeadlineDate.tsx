import { useTranslations } from "next-intl";
import { formatNumericDate } from "@/shared/lib/dates";

export function DeadlineDate({ deadline, locale }: { deadline: { statutory: string; due: string }; locale: string }) {
  const t = useTranslations("deadline");
  const shifted = deadline.statutory !== deadline.due;

  return (
    <div className="flex flex-col">
      <span>{formatNumericDate(deadline.due, locale)}</span>
      {shifted ? (
        <span className="text-xs text-muted-foreground">
          {t("shiftedFrom", { date: formatNumericDate(deadline.statutory, locale) })}
        </span>
      ) : null}
    </div>
  );
}
