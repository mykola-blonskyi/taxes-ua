export function formatMoney(kopecks: number, locale: string) {
  const formatted = new Intl.NumberFormat(locale, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(kopecks / 100);

  return `${formatted} ₴`;
}

export function formatRate(basisPoints: number, locale: string) {
  return new Intl.NumberFormat(locale, {
    style: "percent",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(basisPoints / 10000);
}

// Kopecks are assembled from the digit strings, never by parsing a decimal, so no binary fraction
// such as 0.1 ever stands in for an amount.
const hryvniaPattern = /^\s*(\d[\d\s  ']*)(?:[.,](\d{1,2}))?\s*$/;

export function parseHryvnia(input: string): number | null {
  const match = hryvniaPattern.exec(input);

  if (!match) {
    return null;
  }

  const wholeDigits = match[1].replace(/[\s  ']/g, "");

  // Reject digit strings long enough that whole*100 could lose precision before
  // Number.isSafeInteger below even gets a chance to catch it.
  if (wholeDigits.length === 0 || wholeDigits.length > 14) {
    return null;
  }

  const fractionDigits = (match[2] ?? "").padEnd(2, "0");
  const kopecks = Number(wholeDigits) * 100 + Number(fractionDigits);

  if (!Number.isSafeInteger(kopecks)) {
    return null;
  }

  return kopecks;
}

export function formatMinor(amountMinor: number, currency: string, locale: string) {
  const formatted = new Intl.NumberFormat(locale, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(amountMinor / 100);

  return `${formatted}\u00a0${currency}`;
}

export function formatAmount(amountMinor: number, currency: string, locale: string) {
  return currency === "UAH" ? formatMoney(amountMinor, locale) : formatMinor(amountMinor, currency, locale);
}

export const maxRateE4 = 10_000_000;

// Like parseHryvnia: rateE4 (rate × 10^4) is assembled from the digit strings, never from a float.
const ratePattern = /^\s*(\d+)(?:[.,](\d{1,4}))?\s*$/;

export function parseRate(input: string): number | null {
  const match = ratePattern.exec(input);

  if (!match || match[1].length > 7) {
    return null;
  }

  const rateE4 = Number(match[1]) * 10000 + Number((match[2] ?? "").padEnd(4, "0"));

  return rateE4 === 0 || rateE4 > maxRateE4 ? null : rateE4;
}

export function formatRateE4(rateE4: number, locale: string) {
  return new Intl.NumberFormat(locale, {
    minimumFractionDigits: 4,
    maximumFractionDigits: 4,
  }).format(rateE4 / 10000);
}

// amountMinor (up to ~1e14) times rateE4 (up to 1e7) overflows 2^53, so the product is taken in
// BigInt. Rounds half away from zero like the server; the server's figure is the one that is saved.
export function toUahKop(amountMinor: number, rateE4: number): number {
  const zero = BigInt(0);
  const product = BigInt(amountMinor) * BigInt(rateE4);
  const magnitude = product < zero ? -product : product;
  const rounded = (magnitude + BigInt(5000)) / BigInt(10000);

  return Number(product < zero ? -rounded : rounded);
}

// The plain form a bank form accepts: digits and a dot, no spaces or currency sign.
export function formatPlainAmount(kopecks: number): string {
  const magnitude = Math.abs(kopecks);
  const whole = Math.floor(magnitude / 100);
  const fraction = String(magnitude % 100).padStart(2, "0");

  return `${kopecks < 0 ? "-" : ""}${whole}.${fraction}`;
}
