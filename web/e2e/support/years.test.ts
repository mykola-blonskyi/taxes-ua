import { describe, expect, it } from "vitest";
import { endedFirstQuarterYear } from "./years";

describe("endedFirstQuarterYear", () => {
  it("is last year while the first quarter is running", () => {
    expect(endedFirstQuarterYear("2027-01-01")).toBe(2026);
    expect(endedFirstQuarterYear("2027-03-31")).toBe(2026);
  });

  it("is this year once the first quarter has ended", () => {
    expect(endedFirstQuarterYear("2026-04-01")).toBe(2026);
    expect(endedFirstQuarterYear("2026-10-02")).toBe(2026);
    expect(endedFirstQuarterYear("2026-12-31")).toBe(2026);
  });
});
