import { describe, expect, it } from "vitest";
import {
  formatAmount,
  formatMinor,
  formatMoney,
  formatPlainAmount,
  formatRate,
  formatRateE4,
  maxRateE4,
  parseHryvnia,
  parseRate,
  toUahKop,
} from "./money";

const nbsp = " ";
const narrowNbsp = " ";

// Intl output is the browser's: which space groups thousands, and whether a space precedes % or the currency
// code, differ between ICU releases. The tests pin what the app controls (the digits and their order, the
// decimal comma, the currency sign) and read every kind of space as one.
const plain = (text: string) =>
  text
    .replace(/[\s  ]+/g, " ")
    .replace(/ %$/, "%")
    .trim();

describe("parseHryvnia", () => {
  it.each([
    ["a whole amount", "100", 10_000],
    ["zero", "0", 0],
    ["leading zeros", "007", 700],
    ["a comma decimal", "100,5", 10_050],
    ["a dot decimal", "100.50", 10_050],
    ["two decimals", "0,05", 5],
    ["one decimal digit is tenths, not hundredths", "0,5", 50],
    ["a space as the thousands separator", "1 234,56", 123_456],
    ["a no-break space as the thousands separator", `1${nbsp}234,56`, 123_456],
    ["a narrow no-break space as the thousands separator", `1${narrowNbsp}234${narrowNbsp}567`, 123_456_700],
    ["an apostrophe as the thousands separator", "1'234,56", 123_456],
    ["surrounding whitespace", "  12,30  ", 1_230],
    // 0.1 + 0.2 style sums must never reach the amount: these figures are not exact in binary.
    ["a figure that is inexact as a float", "0,29", 29],
    ["a figure that is inexact as a float, one more", "1,15", 115],
    ["the largest amount whose kopecks stay exact", "90071992547409", 9_007_199_254_740_900],
  ])("reads %s", (_name, input, kopecks) => {
    expect(parseHryvnia(input)).toBe(kopecks);
  });

  it.each([
    ["an empty string", ""],
    ["only whitespace", "   "],
    ["text", "abc"],
    ["a currency sign", "100 ₴"],
    ["a minus sign", "-5"],
    ["a plus sign", "+5"],
    ["a missing whole part", ",5"],
    ["a missing fraction after the separator", "5,"],
    ["three decimals", "10,123"],
    ["a thousands dot with a decimal comma", "1.234,56"],
    ["two separators", "1,2,3"],
    ["an exponent", "1e3"],
    ["a space before the decimal digits", "10, 5"],
    ["one kopeck past the exact range", "90071992547410"],
    ["a 15-digit whole part", "100000000000000"],
  ])("refuses %s", (_name, input) => {
    expect(parseHryvnia(input)).toBeNull();
  });
});

describe("parseRate", () => {
  it.each([
    ["a whole rate", "41", 410_000],
    ["a comma decimal", "41,5", 415_000],
    ["a dot decimal", "41.2345", 412_345],
    ["a small rate", "0,0001", 1],
    ["the largest rate", "1000", maxRateE4],
    ["surrounding whitespace", " 38,75 ", 387_500],
  ])("reads %s", (_name, input, rateE4) => {
    expect(parseRate(input)).toBe(rateE4);
  });

  it.each([
    ["an empty string", ""],
    ["zero", "0"],
    ["zero with decimals", "0,0000"],
    ["text", "abc"],
    ["a negative rate", "-1"],
    ["five decimals", "41,12345"],
    ["a rate over the maximum", "1000,0001"],
    ["a whole part longer than seven digits", "12345678"],
    ["a thousands separator", "1 000"],
  ])("refuses %s", (_name, input) => {
    expect(parseRate(input)).toBeNull();
  });
});

describe("formatMoney", () => {
  it.each([
    ["uk", 0, `0,00${nbsp}₴`],
    ["uk", 5, `0,05${nbsp}₴`],
    ["uk", 123_456, `1${nbsp}234,56${nbsp}₴`],
    ["uk", -50_000, `-500,00${nbsp}₴`],
    ["ru", 123_456, `1${nbsp}234,56${nbsp}₴`],
    ["ru", 1_000_000_000, `10${nbsp}000${nbsp}000,00${nbsp}₴`],
  ])("shows %s kopecks %i", (locale, kopecks, shown) => {
    expect(plain(formatMoney(kopecks, locale))).toBe(plain(shown));
  });
});

describe("formatMinor and formatAmount", () => {
  it("puts a foreign currency code after a no-break space", () => {
    expect(plain(formatMinor(123_456, "USD", "uk"))).toBe("1 234,56 USD");
    expect(formatMinor(123_456, "USD", "uk")).toContain(`${nbsp}USD`);
  });

  it("formats hryvnias with the sign and every other currency with its code", () => {
    expect(formatAmount(100, "UAH", "uk")).toBe(formatMoney(100, "uk"));
    expect(formatAmount(100, "EUR", "uk")).toBe(formatMinor(100, "EUR", "uk"));
  });
});

describe("rate formatting", () => {
  it.each([
    ["uk", 500, "5,00%"],
    ["ru", 500, `5,00${nbsp}%`],
    ["uk", 1_250, "12,50%"],
    ["uk", 0, "0,00%"],
  ])("shows %s basis points %i as a percentage", (locale, basisPoints, shown) => {
    expect(plain(formatRate(basisPoints, locale))).toBe(plain(shown));
  });

  it.each([
    ["uk", 415_000, "41,5000"],
    ["uk", 1, "0,0001"],
    ["ru", 387_525, "38,7525"],
  ])("shows %s rateE4 %i with four decimals", (locale, rateE4, shown) => {
    expect(plain(formatRateE4(rateE4, locale))).toBe(plain(shown));
  });
});

describe("toUahKop", () => {
  it.each([
    ["a whole rate", 10_000, 410_000, 410_000],
    ["rounding down below the half", 1, 4_999, 0],
    ["rounding up at the half", 1, 5_000, 1],
    ["a fractional kopeck above the half", 333, 415_000, 13_820],
    ["a negative amount rounds away from zero at the half", -1, 5_000, -1],
    ["a negative amount rounds down below the half", -1, 4_999, 0],
    ["zero", 0, 415_000, 0],
    ["a product past 2^53 stays exact", 99_999_999_999_999, 10_000_000, 99_999_999_999_999_000],
  ])("converts %s", (_name, amountMinor, rateE4, kopecks) => {
    expect(toUahKop(amountMinor, rateE4)).toBe(kopecks);
  });
});

describe("formatPlainAmount", () => {
  it.each([
    [0, "0.00"],
    [5, "0.05"],
    [50, "0.50"],
    [100, "1.00"],
    [123_456, "1234.56"],
    [10_000_000_000, "100000000.00"],
    [-5, "-0.05"],
    [-100, "-1.00"],
    [-150, "-1.50"],
    [-123_456, "-1234.56"],
  ])("writes %i kopecks as %s", (kopecks, plain) => {
    expect(formatPlainAmount(kopecks)).toBe(plain);
  });

  it("round-trips through parseHryvnia", () => {
    for (const kopecks of [0, 1, 99, 100, 101, 123_456, 9_999_999]) {
      expect(parseHryvnia(formatPlainAmount(kopecks))).toBe(kopecks);
    }
  });
});
