// Building the Date from Y/M/D parts (instead of parsing the ISO string) avoids a UTC-vs-local
// timezone shift moving the displayed day.
export function parseDateOnly(value: string): Date {
  const [year, month, day] = value.split("-").map(Number);

  return new Date(year, month - 1, day);
}

export function formatNumericDate(value: string, locale: string, withWeekday = false): string {
  return new Intl.DateTimeFormat(locale, {
    weekday: withWeekday ? "long" : undefined,
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  }).format(parseDateOnly(value));
}

export function formatDateOnly(value: string, locale: string): string {
  return new Intl.DateTimeFormat(locale, { dateStyle: "medium" }).format(parseDateOnly(value));
}

// Rule 10 dates every operation in Europe/Kyiv, so "today" is the Kyiv calendar day, not the browser's.
export function todayInKyiv(): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Kyiv" }).format(new Date());
}

export function currentYearInKyiv(): number {
  return Number(todayInKyiv().slice(0, 4));
}
