import { randomInt, randomUUID } from "node:crypto";
import { expect, test } from "@playwright/test";
import uk from "../messages/uk.json";
import { dashboard, quarterIncome, seedRegisteredOwner } from "./support/api";

// "1 234,56 ₴" becomes "123456", so a figure compared by its digits ignores which space the locale uses.
const digits = (text: string) => text.replace(/\D/g, "");

test("income added on the transactions screen shows on the dashboard and the periods screen", async ({
  page,
  request,
}) => {
  const { year, quarter } = await seedRegisteredOwner(request);
  const incomeBefore = (await dashboard(request)).burden?.incomeKop ?? 0;
  const quarterBefore = await quarterIncome(request, year, quarter);

  // Whatever other tests left in the book, this amount moves every total by exactly itself.
  const hryvnias = randomInt(1000, 9000);
  const kopecks = randomInt(0, 100);
  const added = hryvnias * 100 + kopecks;

  await page.goto("/transactions");
  await page.getByLabel(uk.transactions.form.amount, { exact: true }).fill(`${hryvnias},${String(kopecks).padStart(2, "0")}`);
  await page.getByLabel(uk.transactions.form.clientName).fill(`E2E ${randomUUID().slice(0, 8)}`);
  const saved = page.waitForResponse(
    (response) => response.request().method() === "POST" && new URL(response.url()).pathname === "/api/transactions",
  );
  await page.getByRole("button", { name: uk.transactions.form.add, exact: true }).click();
  expect((await saved).status()).toBe(201);

  await page.goto("/");
  const burden = page.getByRole("heading", { name: uk.dashboard.burden.title }).locator("..");
  await expect(burden).toBeVisible();
  // The detail line reads "<tax> ... <marker> <income>"; compare only the income figure.
  const marker = uk.dashboard.burden.detail.split("{tax}")[1].split("{income}")[0];
  await expect
    .poll(async () => digits((await burden.getByText(marker).innerText()).split(marker)[1]))
    .toBe(String(incomeBefore + added));

  await page.goto("/periods");
  const quarters = page.getByRole("heading", { name: uk.periods.quartersTitle }).locator("..");
  const row = quarters.getByRole("row").filter({ has: page.getByRole("rowheader", { name: new RegExp(`^Q${quarter}$`) }) });
  await expect.poll(async () => digits(await row.getByRole("cell").first().innerText())).toBe(
    String(quarterBefore + added),
  );
});
