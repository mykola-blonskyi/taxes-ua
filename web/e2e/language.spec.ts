import { expect, test } from "@playwright/test";
import ru from "../messages/ru.json";
import uk from "../messages/uk.json";

test("switching the language changes the interface from Ukrainian to Russian and back", async ({ page }) => {
  await page.goto("/");
  await expect(page.locator("html")).toHaveAttribute("lang", "uk");
  await expect(page.getByRole("navigation", { name: uk.nav.label })).toBeVisible();

  await page.getByRole("button", { name: uk.language.label }).click();
  await page.getByRole("menuitemradio", { name: ru.language.ru }).click();

  await expect(page.locator("html")).toHaveAttribute("lang", "ru");
  const nav = page.getByRole("navigation", { name: ru.nav.label });
  await expect(nav.getByRole("link", { name: ru.nav.payments })).toBeVisible();
  await expect(page.getByText(ru.disclaimer)).toBeVisible();
  await expect(page.getByText(uk.disclaimer)).toBeHidden();

  // The choice is a cookie, so it holds on another screen.
  await page.goto("/settings");
  await expect(page.locator("html")).toHaveAttribute("lang", "ru");

  await page.getByRole("button", { name: ru.language.label }).click();
  await page.getByRole("menuitemradio", { name: uk.language.uk }).click();

  await expect(page.locator("html")).toHaveAttribute("lang", "uk");
  await expect(page.getByRole("navigation", { name: uk.nav.label }).getByRole("link", { name: uk.nav.payments })).toBeVisible();
});

const directions = [
  { from: uk, to: ru, fromLang: "uk", toLang: "ru" },
  { from: ru, to: uk, fromLang: "ru", toLang: "uk" },
] as const;

for (const { from, to, fromLang, toLang } of directions) {
  test(`a language chosen in the menu holds on the very next navigation (${fromLang} to ${toLang})`, async ({ page, context, baseURL }) => {
    await context.addCookies([{ name: "locale", value: fromLang, url: baseURL! }]);
    await page.goto("/");
    await expect(page.locator("html")).toHaveAttribute("lang", fromLang);
    await page.waitForLoadState("networkidle");

    await page.getByRole("button", { name: from.language.label }).click();
    // The owner navigates before anything has settled: the next page is a full load.
    await page.getByRole("menuitemradio", { name: to.language[toLang] }).click();
    await page.goto("/payments");

    await expect(page).toHaveURL(/\/payments/);
    await expect(page.locator("html")).toHaveAttribute("lang", toLang);
    await expect(page.getByText(to.disclaimer)).toBeVisible();
    await expect(page.getByRole("navigation", { name: to.nav.label }).getByRole("link", { name: to.nav.payments })).toBeVisible();
  });
}
