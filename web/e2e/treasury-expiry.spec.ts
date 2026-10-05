import { expect, test, type Page } from "@playwright/test";
import uk from "../messages/uk.json";
import { json, seedRegisteredOwner } from "./support/api";
import { seedTreasuryAccounts, treasuryAccounts, ukrainianIban } from "./support/seed";

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

  await page.goto("/");
  // Another notice may hold the banner slot; the expired account then waits in the folded list.
  const notice = page.getByRole("heading", { name: uk.dashboard.treasuryExpired.titleLevy });
  const folded = page.locator("details > summary");
  await expect(notice.or(folded)).toBeVisible();
  if (await folded.isVisible()) {
    await folded.click();
  }
  await expect(notice).toBeVisible();
  await expect(page.getByRole("link", { name: uk.dashboard.treasuryExpired.cta })).toHaveAttribute(
    "href",
    "/settings?tab=treasury",
  );

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

test("a levy account entered with no end shows its tax year's default end until the owner removes it", async ({
  page,
  request,
}) => {
  const { year } = await seedRegisteredOwner(request);
  await seedTreasuryAccounts(request);
  // The default end is the one of the tax year the account is entered in, which is the current year, so the
  // test gives that year an end when the seeded configuration has none (a new year after the seed). Setting
  // it is idempotent and leaves the year's verification alone.
  const config = await json<Record<string, unknown>>(await request.get(`/api/tax-years/${year}`));
  if (config.militaryLevyAccountEnd === null) {
    const derived = ["year", "esvMonthlyKop", "incomeLimitKop", "verifiedAt"];
    const body = Object.fromEntries(Object.entries(config).filter(([key]) => !derived.includes(key)));
    const set = await request.put(`/api/tax-years/${year}`, { data: { ...body, militaryLevyAccountEnd: `${year}-12-31` } });
    expect(set.ok()).toBe(true);
  }
  // A new IBAN, since entering the same one again keeps whatever the owner said about its end earlier.
  const levy = { ...treasuryAccounts.MilitaryLevy, iban: ukrainianIban(`899998${"9".padStart(19, "0")}`) };
  expect((await request.put("/api/settings/treasury-accounts/MilitaryLevy", { data: levy })).ok()).toBe(true);
  const defaultEnd = uk.settings.treasury.validUntil.default
    .replace("{date}", new Intl.DateTimeFormat("uk", { dateStyle: "medium" }).format(new Date(year, 11, 31)))
    .replace("{year}", String(year));
  const levyCard = () => page.getByRole("heading", { name: uk.payments.kinds.MilitaryLevy }).locator("xpath=ancestor::li[1]");

  await page.goto("/settings?tab=treasury");
  await expect(levyCard().getByText(defaultEnd)).toBeVisible();

  const removed = await request.put("/api/settings/treasury-accounts/MilitaryLevy/valid-until", { data: { validUntil: null } });
  expect(removed.ok()).toBe(true);

  await page.reload();
  await expect(levyCard().getByText(uk.settings.treasury.validUntil.none)).toBeVisible();
  await expect(page.getByText(defaultEnd)).toBeHidden();
});
