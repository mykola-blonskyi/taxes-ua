import { expect, test } from "@playwright/test";
import uk from "../messages/uk.json";
import { signIn } from "./support/api";

test.describe("sign-in gate", () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test("a visitor without a session is sent to the sign-in page", async ({ page }) => {
    await page.goto("/transactions");

    await expect(page).toHaveURL(/\/login/);
    await expect(page.getByRole("heading", { name: uk.login.title })).toBeVisible();
  });

  test("an email outside the allowlist is refused and gets no session", async ({ request }) => {
    const refused = await signIn(request, "stranger@example.com");

    expect(refused.status()).toBe(403);
    expect((await request.get("/api/auth/me")).status()).toBe(401);
  });
});
