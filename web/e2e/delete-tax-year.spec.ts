import { expect, test } from "@playwright/test";
import uk from "../messages/uk.json";
import { seedRegisteredOwner } from "./support/api";

// 2099 is a future year, so the delete is allowed (Rule 9). A 2099 left behind would become the latest year
// the other specs clone from, so it goes first, in case a killed run left it, and last, whatever happens.
const mistaken = 2099;

test("a mistaken unverified year is deleted from the tax years tab, a verified one offers no delete", async ({
  page,
  request,
}) => {
  const { year } = await seedRegisteredOwner(request);
  expect((await request.post(`/api/tax-years/${year}/verify`)).ok()).toBe(true);
  await request.delete(`/api/tax-years/${mistaken}`);
  expect((await request.post(`/api/tax-years/${year}/clone-to/${mistaken}`)).ok()).toBe(true);

  try {
    await page.goto("/settings?tab=taxYears");

    const deleteButton = (target: number) =>
      page.getByRole("button", { name: uk.settings.taxYears.delete.replace("{year}", String(target)) });
    await expect(page.getByRole("button", { name: uk.settings.taxYears.cloneToNext.replace("{next}", String(year + 1)) })).toBeVisible();
    await expect(deleteButton(year)).toHaveCount(0);

    await deleteButton(mistaken).click();
    await expect(page.getByText(uk.settings.taxYears.confirmDelete.replace("{year}", String(mistaken)))).toBeVisible();
    await page.getByRole("button", { name: uk.settings.taxYears.deleteConfirm }).click();

    await expect(page.getByRole("cell", { name: String(mistaken), exact: true })).toHaveCount(0);
    expect((await request.get(`/api/tax-years/${mistaken}`)).status()).toBe(404);
  } finally {
    await request.delete(`/api/tax-years/${mistaken}`);
  }
});
