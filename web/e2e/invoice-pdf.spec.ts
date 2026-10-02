import { readFile } from "node:fs/promises";
import { expect, test } from "@playwright/test";
import uk from "../messages/uk.json";
import { dashboard } from "./support/api";
import { seedIssuedInvoice, seller } from "./support/seed";

test("an issued invoice downloads as a PDF that carries its number and seller", async ({ page, request }) => {
  const { clientName, invoice } = await seedIssuedInvoice(request, (await dashboard(request)).today);

  await page.goto("/invoices");
  await page.getByRole("button", { name: new RegExp(clientName) }).click();
  const download = page.waitForEvent("download");
  await page.getByRole("link", { name: uk.invoices.detail.download }).click();
  const file = await download;

  expect(file.suggestedFilename()).toBe(invoice.pdfFileName);
  const bytes = await readFile(await file.path());
  // The text is in compressed streams under subset fonts, which only a PDF library reads back. The
  // document information dictionary is plain, and it is written from the invoice's own number and seller.
  const raw = bytes.toString("latin1");
  expect(raw.startsWith("%PDF-")).toBe(true);
  expect(raw.trimEnd().endsWith("%%EOF")).toBe(true);
  expect(bytes.length).toBeGreaterThan(10_000);
  expect(raw).toContain(`/Title(Invoice ${invoice.number})`);
  expect(raw).toContain(`/Author(${seller.nameEn})`);
  expect(raw).toMatch(/\/Count 1\b/);
});
