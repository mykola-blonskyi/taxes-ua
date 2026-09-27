import type { PaymentResponse } from "@/data/payments/usePayments";

// The select encodes the api's two nullable period fields as one value, "q1".."q4" or "m1".."m12",
// so the form cannot hold both or neither.
export type PeriodValue = `q${number}` | `m${number}`;

export function toPeriodValue(payment: Pick<PaymentResponse, "periodQuarter" | "periodMonth">): PeriodValue {
  return payment.periodMonth !== null ? `m${Number(payment.periodMonth)}` : `q${Number(payment.periodQuarter)}`;
}

export function fromPeriodValue(value: PeriodValue): { periodQuarter: number | null; periodMonth: number | null } {
  const number = Number(value.slice(1));

  return value.startsWith("m")
    ? { periodQuarter: null, periodMonth: number }
    : { periodQuarter: number, periodMonth: null };
}

export function monthName(month: number, locale: string): string {
  return new Intl.DateTimeFormat(locale, { month: "long", timeZone: "UTC" }).format(Date.UTC(2000, month - 1, 1));
}
