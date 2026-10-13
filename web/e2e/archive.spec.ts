import { readFile } from "node:fs/promises";
import { expect, test } from "@playwright/test";
import uk from "../messages/uk.json";
import { dashboard, seedRegisteredOwner } from "./support/api";
import { seedIssuedInvoice } from "./support/seed";

test("the year's archive is reached from the periods and downloads an issued invoice", async ({ page, request }) => {
  const { today } = await dashboard(request);
  const { year } = await seedRegisteredOwner(request);
  const { clientName, invoice } = await seedIssuedInvoice(request, today);

  await page.goto("/periods");
  await page.getByRole("link", { name: uk.periods.archiveLink.replace("{year}", String(year)) }).click();
  await expect(page).toHaveURL(`/archive?year=${year}`);
  await expect(page.getByText(new RegExp(clientName)).first()).toBeVisible();

  const download = page.waitForEvent("download");
  await page.getByRole("link", { name: uk.archive.download.replace("{name}", invoice.pdfFileName) }).click();
  const file = await download;

  expect(file.suggestedFilename()).toBe(invoice.pdfFileName);
  expect((await readFile(await file.path())).toString("latin1").startsWith("%PDF-")).toBe(true);
});
