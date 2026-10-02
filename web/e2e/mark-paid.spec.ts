import { expect, test } from "@playwright/test";
import uk from "../messages/uk.json";
import { dashboard, json, seedRegisteredOwner } from "./support/api";

type Payment = { paidOn: string; amountKop: number };

function dayBefore(date: string) {
  const [year, month, day] = date.split("-").map(Number);

  return new Date(Date.UTC(year, month - 1, day - 1)).toISOString().slice(0, 10);
}

test("the owner marks the next step paid on the day and for the sum they actually paid", async ({ page, request }) => {
  const { year } = await seedRegisteredOwner(request);
  const { today } = await dashboard(request);
  const paidOn = dayBefore(today);
  // An amount no other test records, so the payment found below is this test's own.
  const amountKop = 12_347;

  await page.setViewportSize({ width: 375, height: 812 });
  await page.goto("/");
  await page.getByRole("button", { name: uk.dashboard.markPaid }).click();
  await expect(page.getByLabel(uk.dashboard.paidOn)).toHaveValue(today);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);

  await page.getByLabel(uk.dashboard.paidOn).fill(today.replace(/^(\d{4})/, (y) => String(Number(y) + 1)));
  await expect(page.getByText(uk.dashboard.paidOnInvalid)).toBeVisible();
  await expect(page.getByRole("button", { name: uk.dashboard.confirm })).toBeDisabled();

  await page.getByLabel(uk.dashboard.paidOn).fill(paidOn);
  await page.getByLabel(uk.dashboard.paidAmount).fill("123,47");
  await page.getByRole("button", { name: uk.dashboard.confirm }).click();
  await expect(page.getByLabel(uk.dashboard.paidOn)).toBeHidden();

  const { items } = await json<{ items: Payment[] }>(await request.get(`/api/payments?year=${year}`));
  expect(items.filter((item) => item.amountKop === amountKop).map((item) => item.paidOn)).toEqual([paidOn]);
});
