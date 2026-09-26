import { useTranslations } from "next-intl";
import type { PeriodsResponse } from "@/data/periods/usePeriods";

type Deadline = PeriodsResponse["quarters"][number]["deadlines"]["esv"];

function formatDate(value: string, locale: string): string {
  return new Intl.DateTimeFormat(locale, { day: "2-digit", month: "2-digit", year: "numeric", timeZone: "UTC" }).format(
    new Date(`${value}T00:00:00Z`),
  );
}

export function DeadlineDate({ deadline, locale }: { deadline: Deadline; locale: string }) {
  const t = useTranslations("periods");
  const shifted = deadline.statutory !== deadline.due;

  return (
    <div className="flex flex-col">
      <span>{formatDate(deadline.due, locale)}</span>
      {shifted ? (
        <span className="text-xs text-muted-foreground">
          {t("shiftedFrom", { date: formatDate(deadline.statutory, locale) })}
        </span>
      ) : null}
    </div>
  );
}
