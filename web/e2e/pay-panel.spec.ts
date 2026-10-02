import { expect, test } from "@playwright/test";
import uk from "../messages/uk.json";
import { seedRegisteredOwner } from "./support/api";
import { seedTreasuryAccounts, treasuryAccounts } from "./support/seed";

test("the pay panel for an obligation shows the Treasury account, the purpose and a QR", async ({ page, request }) => {
  const { year, quarter } = await seedRegisteredOwner(request);
  await seedTreasuryAccounts(request);
  const esv = treasuryAccounts.Esv;

  await page.goto("/payments");
  const quarterCard = page
    .getByRole("heading", { name: uk.dashboard.quarter.replace("{quarter}", String(quarter)).replace("{year}", String(year)) })
    .locator("..");
  await quarterCard
    .getByRole("listitem")
    .filter({ hasText: uk.payments.kinds.Esv })
    .getByRole("button", { name: uk.pay.button })
    .click();

  const panel = page.getByRole("dialog");
  // An amount of its own, so the test does not depend on what the obligation has left to pay.
  const detailsLoaded = page.waitForResponse((response) => {
    const url = new URL(response.url());
    return url.pathname === "/api/payment-details" && url.searchParams.get("amountKop") === "123456";
  });
  await panel.getByLabel(uk.pay.amountInput).fill("1234,56");
  const details = (await (await detailsLoaded).json()) as { purpose: string; qrContent: string };

  await expect(panel.getByText(esv.recipientName, { exact: true })).toBeVisible();
  await expect(panel.getByText(esv.recipientCode, { exact: true })).toBeVisible();
  await expect(panel.getByText(esv.iban, { exact: true })).toBeVisible();
  await expect(panel.getByText("1234.56", { exact: true })).toBeVisible();
  await expect(panel.getByText(details.purpose, { exact: true })).toBeVisible();
  // The payload behind the QR names the same recipient, account and purpose the panel prints.
  // The QR is a bank.gov.ua link whose last segment is the base64url payload.
  const payload = Buffer.from(details.qrContent.split("/").pop()!, "base64url").toString("utf8");
  expect(payload).toContain(esv.recipientName);
  expect(payload).toContain(esv.iban);
  expect(payload).toContain(`UAH1234.56`);
  expect(payload).toContain(esv.recipientCode);
  expect(payload).toContain(details.purpose);
  await expect(panel.getByRole("img", { name: uk.pay.qrLabel })).toBeVisible();
  await expect(panel.getByText(uk.pay.qrUpdating)).toBeHidden();
});
