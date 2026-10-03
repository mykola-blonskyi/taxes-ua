import { expect, test } from "@playwright/test";
import uk from "../messages/uk.json";
import { taxOffice } from "./support/seed";

// The classifier is the api's: the form shows Держстат's name under a code, and a code of the right shape
// that is not a class is worded as unknown before anything is saved.
test("the declaration details name the saved KVED and word an unknown one", async ({ page, request }) => {
  const saved = await request.put("/api/settings/declaration", {
    data: {
      taxOfficeRegion: taxOffice.region,
      taxOfficeDistrict: taxOffice.district,
      taxOfficeName: taxOffice.name,
      kvedCodes: ["62.01"],
      address: taxOffice.address,
    },
  });
  expect(saved.ok()).toBe(true);

  await page.goto("/settings?tab=declaration");

  const main = page.getByLabel(uk.settings.declaration.kvedCode.replace("{number}", "1"));
  await expect(main).toHaveAccessibleDescription("Комп'ютерне програмування");

  await page.getByRole("button", { name: uk.settings.declaration.addKved }).click();
  const extra = page.getByLabel(uk.settings.declaration.kvedCode.replace("{number}", "2"), { exact: true });
  await extra.fill("85.59");
  await expect(page.getByText("Інші види освіти, н.в.і.у.")).toBeVisible();

  await extra.fill("12.34");
  await expect(page.getByText(uk.apiErrors.kved_unknown)).toBeVisible();
});
