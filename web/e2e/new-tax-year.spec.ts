import { expect, test } from "@playwright/test";
import uk from "../messages/uk.json";
import { json, seedRegisteredOwner } from "./support/api";

// The dashboard notice only exists in December, which the stack's real clock cannot be moved to; its
// reasoning is covered by the api and component tests. What it links to is the same address every day, so
// the scenario opens it and checks what the owner finds: the tax years tab, with the clone offered.
// Cloning is not clicked: the specs share one owner, and a row for next year would change every ledger
// the later specs read.
test("the tax years tab offers to clone this year into the coming one", async ({ page, request }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  const { year } = await seedRegisteredOwner(request);
  const taxYears = await json<{ year: number }[]>(await request.get("/api/tax-years"));
  expect(taxYears.map((taxYear) => taxYear.year)).not.toContain(year + 1);

  await page.goto("/settings?tab=taxYears");

  await expect(page.getByRole("tab", { name: uk.settings.tabs.taxYears })).toHaveAttribute("aria-selected", "true");
  const offer = page.getByRole("button", {
    name: uk.settings.taxYears.offer.clone.replace("{year}", String(year)).replace("{next}", String(year + 1)),
  });
  await expect(offer).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
});
