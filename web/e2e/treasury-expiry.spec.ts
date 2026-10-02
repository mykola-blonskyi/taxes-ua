import { expect, test, type Page } from "@playwright/test";
import uk from "../messages/uk.json";
import { seedRegisteredOwner } from "./support/api";
import { seedTreasuryAccounts, treasuryAccounts } from "./support/seed";

async function openLevyPayPanel(page: Page, year: number, quarter: number) {
  await page.goto("/payments");
  await page
    .getByRole("heading", { name: uk.dashboard.quarter.replace("{quarter}", String(quarter)).replace("{year}", String(year)) })
    .locator("..")
    .getByRole("listitem")
    .filter({ hasText: uk.payments.kinds.MilitaryLevy })
    .getByRole("button", { name: uk.pay.button })
    .click();

  return page.getByRole("dialog");
}

// An end in the past is expired for any payment, whatever today is, so the scenario needs no clock.
test("an expired Treasury account hides the details and the QR in the pay panel and leads to settings", async ({
  page,
  request,
}) => {
  await page.setViewportSize({ width: 375, height: 812 });
  const { year, quarter } = await seedRegisteredOwner(request);
  await seedTreasuryAccounts(request);
  const levy = treasuryAccounts.MilitaryLevy;
  const ended = await request.put("/api/settings/treasury-accounts/MilitaryLevy", {
    data: { ...levy, validUntil: "2020-12-31" },
  });
  expect(ended.ok()).toBe(true);

  const panel = await openLevyPayPanel(page, year, quarter);

  await expect(panel.getByText(uk.pay.expiredSettingsLink)).toBeVisible();
  await expect(panel.getByText(levy.iban)).toBeHidden();
  await expect(panel.getByRole("button", { name: /Копіювати/ })).toHaveCount(0);
  await expect(panel.getByRole("img", { name: uk.pay.qrLabel })).toBeHidden();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
  await page.screenshot({ path: "../.verify/treasury-expired-pay-panel-375.png" });

  await panel.getByRole("link", { name: uk.pay.expiredSettingsLink }).click();
  await expect(page).toHaveURL(/\/settings\?tab=treasury/);
  await expect(page.getByText("31 груд. 2020 р.")).toBeVisible();
});

test("an account whose end the owner removes is offered again", async ({ page, request }) => {
  const { year, quarter } = await seedRegisteredOwner(request);
  await seedTreasuryAccounts(request);
  const levy = treasuryAccounts.MilitaryLevy;
  await request.put("/api/settings/treasury-accounts/MilitaryLevy", { data: { ...levy, validUntil: "2020-12-31" } });
  const cleared = await request.put("/api/settings/treasury-accounts/MilitaryLevy/valid-until", { data: { validUntil: null } });
  expect(cleared.ok()).toBe(true);

  const panel = await openLevyPayPanel(page, year, quarter);

  await expect(panel.getByText(levy.iban, { exact: true })).toBeVisible();
  await expect(page.getByText(uk.pay.expiredSettingsLink)).toBeHidden();
});
