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
