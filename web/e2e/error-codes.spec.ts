import { expect, test } from "@playwright/test";
import uk from "../messages/uk.json";

// ADR-028: the api names a failure by a stable code and the page words it, in the owner's language, from
// the code alone. Only the real stack shows that the api's code survives the rewrite and the client.
const clients = "/api/clients";

test("a rejected request carries a code and a code for each rejected field", async ({ request }) => {
  const response = await request.get("/api/payments?year=1");

  expect(response.status()).toBe(400);
  const problem = (await response.json()) as { code: string; errors: Record<string, string[]>; errorCodes: Record<string, string[]> };
  expect(problem.code).toBe("validation_failed");
  expect(problem.errorCodes).toEqual({ year: ["year_out_of_range"] });
  expect(problem.errors.year).toHaveLength(1);
});

test("a taken client name is worded from its code, not from the api's English", async ({ page, request }) => {
  const name = `Error codes ${Date.now()}`;
  expect((await request.post(clients, { data: { name } })).ok()).toBe(true);

  await page.goto("/settings?tab=clients");
  await page.getByRole("button", { name: uk.settings.clients.add }).click();
  await page.getByLabel(new RegExp(`^${uk.settings.clients.name}`)).fill(name);
  await page.getByRole("button", { name: uk.settings.clients.save }).click();

  await expect(page.getByText(uk.apiErrors.name_taken)).toBeVisible();
  await expect(page.getByText(uk.settings.clients.validationError)).toBeVisible();
  await expect(page.getByText(/already exists/)).toHaveCount(0);
});
