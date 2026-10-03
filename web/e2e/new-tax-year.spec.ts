import { expect, test } from "@playwright/test";
import uk from "../messages/uk.json";
import { dashboard, json, seedRegisteredOwner } from "./support/api";

// The dashboard notice only exists in December, which the stack's real clock cannot be moved to; its
// reasoning is covered by the api and component tests. What it links to is the same address every day, so
// the scenario opens it and checks what the owner finds: the tax years tab, with the clone offered at the
// top when the next year is due (December in Kyiv, or the current year missing) and on the row otherwise.
// Cloning is not clicked: the specs share one owner, and a row for next year would change every ledger
// the later specs read.
test("the tax years tab offers the clone of the latest year into the coming one", async ({ page, request }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await seedRegisteredOwner(request);
  const { today } = await dashboard(request);
  const taxYears = await json<{ year: number }[]>(await request.get("/api/tax-years"));
  const latest = Math.max(...taxYears.map((taxYear) => taxYear.year));
  const next = latest + 1;
  expect(Number(today.slice(0, 4))).toBeGreaterThanOrEqual(latest);

  await page.goto("/settings?tab=taxYears");

  await expect(page.getByRole("tab", { name: uk.settings.tabs.taxYears })).toHaveAttribute("aria-selected", "true");
  const offer = page.getByRole("button", {
    name: uk.settings.taxYears.offer.clone.replace("{year}", String(latest)).replace("{next}", String(next)),
  });
  if (today.slice(5, 7) === "12" && next <= Number(today.slice(0, 4)) + 1) {
    await expect(offer).toBeVisible();
  } else {
    await expect(offer).toHaveCount(0);
  }
  await expect(
    page.getByRole("button", { name: uk.settings.taxYears.cloneToNext.replace("{next}", String(next)) }),
  ).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
});
