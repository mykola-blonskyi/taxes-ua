import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { NbuQrCode, encodeNbuQr } from "./nbu-qr";

const utf8Length = (text: string) => new TextEncoder().encode(text).length;

// The api caps a link at 507 bytes: 23 for the start code and at most 475 for the encoded block.
const longestLinkFromApi = 23 + 475;

describe("encodeNbuQr", () => {
  it.each([
    ["a short link", "a".repeat(10), 10, "Q"],
    ["the most version 10 holds at level Q", "a".repeat(151), 10, "Q"],
    ["one byte more moves up a version", "a".repeat(152), 11, "Q"],
    ["the most level Q holds at version 17", "a".repeat(364), 17, "Q"],
    ["one byte more falls back to level M", "a".repeat(365), 15, "M"],
    ["the longest link the api sends", "a".repeat(longestLinkFromApi), 17, "M"],
    ["the most level M holds at version 17", "a".repeat(504), 17, "M"],
  ])("encodes %s", (_name, content, version, level) => {
    const qr = encodeNbuQr(content);

    expect(qr).not.toBeNull();
    expect(qr!.version).toBe(version);
    expect(qr!.level).toBe(level);
    expect(qr!.size).toBe(17 + 4 * version);
  });

  it("never picks a version below 10, which the NBU format requires", () => {
    expect(encodeNbuQr("")!.version).toBe(10);
    expect(encodeNbuQr("a")!.version).toBe(10);
  });

  it("gives no code for content longer than version 17 at level M holds", () => {
    expect(encodeNbuQr("a".repeat(505))).toBeNull();
    expect(encodeNbuQr("a".repeat(2_000))).toBeNull();
  });

  describe("Cyrillic", () => {
    it("counts bytes, not letters, against the limit", () => {
      const fits = "Ж".repeat(250);
      const tooLong = "Ж".repeat(253);

      expect(fits.length).toBeLessThan(504);
      expect(utf8Length(fits)).toBe(500);
      expect(encodeNbuQr(fits)).not.toBeNull();

      expect(tooLong.length).toBeLessThan(504);
      expect(utf8Length(tooLong)).toBe(506);
      expect(encodeNbuQr(tooLong)).toBeNull();
    });
  });

  it("gives the same code for the same content and a different one for different content", () => {
    const grid = (content: string) => {
      const qr = encodeNbuQr(content)!;

      return Array.from({ length: qr.size }, (_, row) =>
        Array.from({ length: qr.size }, (_, col) => (qr.isDark(row, col) ? "1" : "0")).join(""),
      ).join("\n");
    };

    expect(grid("https://qr.bank.gov.ua/abc")).toBe(grid("https://qr.bank.gov.ua/abc"));
    expect(grid("https://qr.bank.gov.ua/abc")).not.toBe(grid("https://qr.bank.gov.ua/abd"));
  });

  it("starts every code with the dark corner of a finder pattern", () => {
    const qr = encodeNbuQr("https://qr.bank.gov.ua/abc")!;

    expect(qr.isDark(0, 0)).toBe(true);
    expect(qr.isDark(0, qr.size - 1)).toBe(true);
    expect(qr.isDark(qr.size - 1, 0)).toBe(true);
  });
});

describe("NbuQrCode", () => {
  it("draws a labelled image for content that can be encoded", () => {
    const html = renderToStaticMarkup(<NbuQrCode content="https://qr.bank.gov.ua/abc" label="Payment QR code" />);

    expect(html).toContain('role="img"');
    expect(html).toContain('aria-label="Payment QR code"');
    expect(html).toContain("₴");
  });

  it("draws nothing for content that cannot be encoded", () => {
    expect(renderToStaticMarkup(<NbuQrCode content={"a".repeat(505)} label="Payment QR code" />)).toBe("");
  });
});
