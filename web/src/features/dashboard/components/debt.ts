import type { KindDebt } from "@/data/dashboard/useDashboard";
import { parseDateOnly } from "@/shared/lib/dates";

export function formatLongDate(value: string, today: string, locale: string): string {
  const sameYear = value.slice(0, 4) === today.slice(0, 4);

  // A deadline read across two lines ("19 | лютого") is easy to misread on a phone.
  return new Intl.DateTimeFormat(locale, {
    day: "numeric",
    month: "long",
    year: sameYear ? undefined : "numeric",
  })
    .format(parseDateOnly(value))
    .replace(/ /g, " ");
}

export function periodOf(debt: KindDebt) {
  return {
    fromYear: Number(debt.fromYear),
    fromQuarter: Number(debt.fromQuarter),
    toYear: Number(debt.toYear),
    toQuarter: Number(debt.toQuarter),
  };
}

export function quarterEndOf(debt: KindDebt): string {
  const { toYear, toQuarter } = periodOf(debt);

  return new Date(Date.UTC(toYear, 3 * toQuarter, 0)).toISOString().slice(0, 10);
}
