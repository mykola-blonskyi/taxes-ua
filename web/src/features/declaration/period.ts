export type Period = { year: number; quarter: number };

export function lastEndedQuarter(today: string): Period {
  const year = Number(today.slice(0, 4));
  const current = Math.ceil(Number(today.slice(5, 7)) / 3);

  return current === 1 ? { year: year - 1, quarter: 4 } : { year, quarter: current - 1 };
}

export function periodFrom(year: string | undefined, quarter: string | undefined, today: string): Period {
  return /^\d{4}$/.test(year ?? "") && /^[1-4]$/.test(quarter ?? "")
    ? { year: Number(year), quarter: Number(quarter) }
    : lastEndedQuarter(today);
}

export function shiftQuarter({ year, quarter }: Period, by: 1 | -1): Period {
  const index = year * 4 + quarter - 1 + by;

  return { year: Math.floor(index / 4), quarter: (index % 4) + 1 };
}

export function isAfter(a: Period, b: Period): boolean {
  return a.year * 4 + a.quarter > b.year * 4 + b.quarter;
}

export function dayAfterQuarter({ year, quarter }: Period): string {
  return new Date(Date.UTC(year, 3 * quarter, 1)).toISOString().slice(0, 10);
}

export function daysBetween(from: string, to: string): number {
  return (Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000;
}
