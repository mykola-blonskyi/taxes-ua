import { readFile } from "node:fs/promises";
import { expect, test } from "@playwright/test";
import uk from "../messages/uk.json";
import { seedDeclarationReady, seller, taxOffice } from "./support/seed";

test("the declaration XML downloads under the DPS file name and parses as form F0103309", async ({ page, request }) => {
  const { year } = await seedDeclarationReady(request);
  // The first quarter has no annex 1, so the download is the one declaration file whenever the suite runs.
  const quarter = 1;

  await page.goto(`/declaration?year=${year}&quarter=${quarter}`);
  const download = page.waitForEvent("download");
  await page.getByRole("button", { name: uk.declaration.xml.download }).click();
  const file = await download;

  // Standard No. 729: region, district, RNOKPP, form code, state, 00, sequence, period type (2 is a quarter),
  // the last month of the quarter, year and the tax office code.
  const parts = [
    String(taxOffice.region),
    String(taxOffice.district).padStart(2, "0"),
    seller.rnokpp,
    "F0103309",
    "1",
    "00",
    "0000001",
    "2",
    String(quarter * 3).padStart(2, "0"),
    String(year),
    `${taxOffice.region}${String(taxOffice.district).padStart(2, "0")}`,
  ];
  expect(file.suggestedFilename()).toBe(`${parts.join("")}.xml`);

  // The file is windows-1251, which is what the Electronic Cabinet reads: the prolog says so, and the
  // Cyrillic header values only read back right when the bytes are decoded as that.
  const bytes = await readFile(await file.path());
  expect(bytes.subarray(0, 60).toString("latin1")).toContain('<?xml version="1.0" encoding="windows-1251"?>');
  const xml = new TextDecoder("windows-1251").decode(bytes);
  const parsed = await page.evaluate((text) => {
    const document = new DOMParser().parseFromString(text, "application/xml");
    const read = (tag: string) => document.querySelector(tag)?.textContent ?? null;
    return {
      wellFormed: document.querySelector("parsererror") === null,
      root: document.documentElement.tagName,
      doc: read("C_DOC"),
      sub: read("C_DOC_SUB"),
      version: read("C_DOC_VER"),
      tin: read("TIN"),
      year: read("PERIOD_YEAR"),
      periodType: read("PERIOD_TYPE"),
      periodMonth: read("PERIOD_MONTH"),
      taxOfficeName: read("HSTI"),
      address: read("HLOC"),
    };
  }, xml);

  expect(parsed).toEqual({
    wellFormed: true,
    root: "DECLAR",
    doc: "F01",
    sub: "033",
    version: "9",
    tin: seller.rnokpp,
    year: String(year),
    periodType: "2",
    periodMonth: String(quarter * 3),
    taxOfficeName: taxOffice.name,
    address: taxOffice.address,
  });
});
