import { readFile } from "node:fs/promises";
import { expect, test } from "@playwright/test";
import uk from "../messages/uk.json";
import { dashboard, seedRegisteredOwner } from "./support/api";
import { seedFinishedYear, seedIssuedInvoice } from "./support/seed";

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

// Entry names from the ZIP's central directory (end-of-central-directory record, then each file header).
function zipEntryNames(zip: Buffer): string[] {
  const end = zip.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]));
  expect(end).toBeGreaterThanOrEqual(0);
  const count = zip.readUInt16LE(end + 10);
  let offset = zip.readUInt32LE(end + 16);
  const names: string[] = [];
  for (let index = 0; index < count; index += 1) {
    expect(zip.readUInt32LE(offset)).toBe(0x02014b50);
    const nameLength = zip.readUInt16LE(offset + 28);
    const extraLength = zip.readUInt16LE(offset + 30);
    const commentLength = zip.readUInt16LE(offset + 32);
    names.push(zip.subarray(offset + 46, offset + 46 + nameLength).toString("utf8"));
    offset += 46 + nameLength + extraLength + commentLength;
  }
  return names;
}

test("the archive downloads the whole year as one ZIP with its contents file", async ({ page, request }) => {
  const { today } = await dashboard(request);
  const { year } = await seedRegisteredOwner(request);
  const { invoice } = await seedIssuedInvoice(request, today);

  await page.goto(`/archive?year=${year}`);
  const download = page.waitForEvent("download");
  await page.getByRole("link", { name: uk.archive.zip.download }).click();
  const file = await download;

  expect(file.suggestedFilename()).toMatch(new RegExp(`^archive-${year}-\\d{4}-\\d{2}-\\d{2}\\.zip$`));
  const bytes = await readFile(await file.path());
  expect(bytes.subarray(0, 2).toString("latin1")).toBe("PK");
  const names = zipEntryNames(bytes);
  expect(names).toContain("contents.txt");
  expect(names).toContain(`invoices/${invoice.pdfFileName}`);
});

test("the last filing mark of a year raises the archive notice, and downloading the year's ZIP clears it", async ({
  page,
  request,
}) => {
  const { year, undo } = await seedFinishedYear(request);
  const title = uk.dashboard.archive.title.replace("{year}", String(year));

  try {
    await page.goto("/");
    await expect(page.getByRole("heading", { name: uk.dashboard.invoices.title })).toBeVisible();
    const fold = page.locator("main details > summary");
    // Another notice may hold the banner, and a closed fold hides what it holds from a role query.
    if (await fold.isVisible()) {
      await fold.click();
    }
    const notice = page.getByRole("status").filter({ has: page.getByRole("heading", { name: title }) });
    await expect(notice).toBeVisible();

    await notice.getByRole("link", { name: uk.dashboard.archive.cta }).click();
    await expect(page).toHaveURL(`/archive?year=${year}`);
    const download = page.waitForEvent("download");
    await page.getByRole("link", { name: uk.archive.zip.download }).click();
    const file = await download;
    expect(file.suggestedFilename()).toMatch(new RegExp(`^archive-${year}-`));
    expect(await file.path()).toBeTruthy();

    await page.goto("/");
    await expect(page.getByRole("heading", { name: uk.dashboard.invoices.title })).toBeVisible();
    if (await fold.isVisible()) {
      await fold.click();
    }
    await expect(page.getByRole("heading", { name: title })).toHaveCount(0);
  } finally {
    await undo(request);
  }
});
