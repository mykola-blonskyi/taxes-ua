import { afterEach, describe, expect, it, vi } from "vitest";
import {
  currentYearInKyiv,
  formatDateOnly,
  formatInstantInKyiv,
  formatMonthInKyiv,
  formatNumericDate,
  parseDateOnly,
  todayInKyiv,
} from "./dates";

afterEach(() => {
  vi.useRealTimers();
});

describe("parseDateOnly", () => {
  it.each([
    ["2026-01-01", 2026, 0, 1],
    ["2026-12-31", 2026, 11, 31],
    ["2028-02-29", 2028, 1, 29],
    ["2026-03-29", 2026, 2, 29],
  ])("keeps %s on the same calendar day", (value, year, month, day) => {
    const date = parseDateOnly(value);

    expect([date.getFullYear(), date.getMonth(), date.getDate()]).toEqual([year, month, day]);
  });
});

describe("formatNumericDate", () => {
  it.each([
    ["uk", "2026-01-05", false, "05.01.2026"],
    ["ru", "2026-01-05", false, "05.01.2026"],
    ["uk", "2026-12-31", false, "31.12.2026"],
    ["uk", "2028-02-29", false, "29.02.2028"],
    ["uk", "2026-01-05", true, "понеділок, 05.01.2026"],
    ["ru", "2026-01-05", true, "понедельник, 05.01.2026 г."],
  ])("shows %s %s (weekday %s)", (locale, value, withWeekday, shown) => {
    expect(formatNumericDate(value, locale, withWeekday)).toBe(shown);
  });
});

describe("formatDateOnly", () => {
  it.each([
    ["uk", "2026-01-05", "січ"],
    ["ru", "2026-01-05", "янв"],
    ["uk", "2026-12-31", "груд"],
  ])("writes the %s month of %s in words, on the same day", (locale, value, month) => {
    const shown = formatDateOnly(value, locale);

    expect(shown).toContain(month);
    expect(shown).toContain(value.slice(0, 4));
    expect(shown).toContain(String(Number(value.slice(8))));
  });
});

describe("todayInKyiv", () => {
  it.each([
    ["before midnight in Kyiv, summer", "2026-07-15T20:59:59Z", "2026-07-15"],
    ["after midnight in Kyiv, summer", "2026-07-15T21:00:00Z", "2026-07-16"],
    ["before midnight in Kyiv, winter", "2026-01-15T21:59:59Z", "2026-01-15"],
    ["after midnight in Kyiv, winter", "2026-01-15T22:00:00Z", "2026-01-16"],
    ["the New Year already in Kyiv", "2026-12-31T22:00:00Z", "2027-01-01"],
    ["the day the clocks go forward", "2026-03-29T00:30:00Z", "2026-03-29"],
  ])("gives the Kyiv day for %s", (_name, instant, day) => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(instant));

    expect(todayInKyiv()).toBe(day);
  });

  it("gives the Kyiv year across New Year", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-12-31T22:00:00Z"));

    expect(currentYearInKyiv()).toBe(2027);
  });
});

describe("formatMonthInKyiv", () => {
  it.each([
    ["uk", "2026-03-31T22:30:00Z", "квітень 2026"],
    ["ru", "2026-03-31T22:30:00Z", "апрель 2026"],
    ["uk", "2026-03-31T20:30:00Z", "березень 2026"],
    ["uk", "2026-12-31T22:30:00Z", "січень 2027"],
  ])("names the Kyiv month for %s %s", (locale, instant, shown) => {
    expect(formatMonthInKyiv(instant, locale)).toContain(shown);
  });
});

describe("formatInstantInKyiv", () => {
  it.each([
    ["winter time, UTC+2", "2026-01-15T10:00:00Z", "12:00"],
    ["summer time, UTC+3", "2026-07-15T10:00:00Z", "13:00"],
    ["just past midnight in Kyiv", "2026-03-31T22:30:00Z", "01:30"],
  ])("writes the Kyiv clock time in %s", (_name, instant, time) => {
    expect(formatInstantInKyiv(instant, "uk")).toContain(time);
  });

  it("writes the Kyiv calendar day, not the viewer's", () => {
    expect(formatInstantInKyiv("2026-03-31T22:30:00Z", "uk")).toContain("1 квіт");
  });
});
