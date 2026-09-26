export function formatMoney(kopecks: number, locale: string) {
  const formatted = new Intl.NumberFormat(locale, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(kopecks / 100);

  return `${formatted} ₴`;
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
