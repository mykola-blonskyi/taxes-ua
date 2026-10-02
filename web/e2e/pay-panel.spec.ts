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
  // The QR is the NBU start code followed by the base64url of seventeen LF-separated fields (format 003,
  // NbuQr.cs); the image itself is not decoded.
  const startCode = "https://qr.bank.gov.ua/";
  expect(details.qrContent.startsWith(startCode)).toBe(true);
  const fields = Buffer.from(details.qrContent.slice(startCode.length), "base64url").toString("utf8").split("\n");
  expect(fields).toHaveLength(17);
  expect(fields[0]).toBe("BCD");
  expect(fields[1]).toBe("003");
  expect(fields[5]).toBe(esv.recipientName);
  expect(fields[6]).toBe(esv.iban);
  expect(fields[7]).toBe("UAH1234.56");
  expect(fields[8]).toBe(esv.recipientCode);
  expect(fields[11]).toBe(details.purpose);
  await expect(panel.getByRole("img", { name: uk.pay.qrLabel })).toBeVisible();
  await expect(panel.getByText(uk.pay.qrUpdating)).toBeHidden();
});
