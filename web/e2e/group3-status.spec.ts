import { expect, test } from "@playwright/test";
import uk from "../messages/uk.json";
import { dashboard, json, seedRegisteredOwner } from "./support/api";

type DpsStatus = {
  group3Since: string | null;
  confirmation: { confirmedOn: string; receiptNumber: string } | null;
  fopRegistered: boolean;
  esvRegistered: boolean;
  accountsRegistered: boolean;
};

const unconfirmed: DpsStatus = {
  group3Since: null,
  confirmation: null,
  fopRegistered: false,
  esvRegistered: false,
  accountsRegistered: false,
};

test("the owner confirms group 3 from the dashboard banner and the banner goes", async ({ page, request }) => {
  await seedRegisteredOwner(request);
  const { today } = await dashboard(request);
  const before = await json<DpsStatus>(await request.get("/api/settings/dps-status"));
  expect((await request.put("/api/settings/dps-status", { data: unconfirmed })).ok()).toBe(true);

  try {
    await page.goto("/");
    const banner = page.getByRole("status").filter({ hasText: uk.dashboard.group3.unconfirmed });
    await expect(banner).toBeVisible();

    await banner.getByRole("link", { name: uk.dashboard.group3.checkStatus }).click();
    await expect(page).toHaveURL(/\/settings\?tab=dps$/);
    await expect(page.getByRole("tab", { name: uk.settings.tabs.dps })).toHaveAttribute("aria-selected", "true");

    await page.getByRole("checkbox", { name: uk.settings.dps.fopRegistered }).check();
    await page.getByRole("checkbox", { name: uk.settings.dps.group3Accepted }).check();
    await page.getByLabel(uk.settings.dps.confirmedOn).fill(today);
    await page.getByLabel(uk.settings.dps.receiptNumber).fill("9154001234");
    await page.getByRole("checkbox", { name: uk.settings.dps.esvRegistered }).check();
    await page.getByRole("checkbox", { name: uk.settings.dps.accountsRegistered }).check();
    await page.getByRole("button", { name: uk.settings.save }).click();
    await expect(page.getByRole("status").filter({ hasText: uk.settings.dps.saved })).toBeVisible();

    expect(await json<DpsStatus>(await request.get("/api/settings/dps-status"))).toMatchObject({
      group3Since: null,
      confirmation: { confirmedOn: today, receiptNumber: "9154001234" },
      fopRegistered: true,
      esvRegistered: true,
      accountsRegistered: true,
    });

    await page.goto("/");
    // The invoices card paints with the dashboard's data, so the banner's absence is measured on a loaded screen.
    await expect(page.getByRole("heading", { name: uk.dashboard.invoices.title })).toBeVisible();
    await expect(page.getByText(uk.dashboard.group3.unconfirmed)).toHaveCount(0);
  } finally {
    // The specs share one owner, so the status goes back to what this test found.
    const { group3Since, confirmation, fopRegistered, esvRegistered, accountsRegistered } = before;
    const restored = await request.put("/api/settings/dps-status", {
      data: { group3Since, confirmation, fopRegistered, esvRegistered, accountsRegistered },
    });
    expect(restored.ok()).toBe(true);
  }
});
