import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import ru from "../../../messages/ru.json";
import uk from "../../../messages/uk.json";

// ADR-028: the api names every failure with a code from ProblemCodes.cs, and the web has the words for each
// of them in both languages. The C# file is the list; this keeps the two catalogs in step with it.
const source = readFileSync(resolve(process.cwd(), "../api/src/TaxesUa.Api/ProblemCodes.cs"), "utf8");
const codes = [...source.matchAll(/public const string \w+ = "([a-z0-9_]+)";/g)].map((match) => match[1]!);

describe("the api error catalog", () => {
  it("reads the codes the api defines", () => {
    expect(codes.length).toBeGreaterThan(100);
    expect(new Set(codes).size).toBe(codes.length);
    expect(codes).toContain("cross_site_request");
  });

  it.each([
    ["Ukrainian", uk.apiErrors],
    ["Russian", ru.apiErrors],
  ])("has %s words for every code, for the generic and network sentences, and for nothing else", (_language, catalog) => {
    const words = catalog as Record<string, string>;

    expect(Object.keys(words).sort()).toEqual([...codes, "unknown", "network"].sort());
    for (const [code, text] of Object.entries(words)) {
      expect(text.trim(), code).not.toBe("");
      expect(text, code).not.toMatch(/[{}]/);
      expect(text, code).toMatch(/[А-ЯҐЄІЇа-яґєії]/);
    }
  });

  it("words nothing the same in both languages by accident of an untranslated copy", () => {
    const ukWords = uk.apiErrors as Record<string, string>;
    const ruWords = ru.apiErrors as Record<string, string>;
    const copies = codes.filter((code) => ukWords[code] === ruWords[code]);

    expect(copies).toEqual([]);
  });
});
