import { expect, test } from "@playwright/test";
import uk from "../messages/uk.json";

// The stack starts with neither a bot token nor SMTP settings (global setup blanks them), so both
// channels are unavailable and offer no way to connect.
test("without a bot token or SMTP settings the Telegram and email channels show as unavailable", async ({
  page,
  request,
}) => {
  const channels = (await (await request.get("/api/notifications/channels")).json()) as { kind: string; available: boolean }[];
  expect(channels.map(({ kind, available }) => [kind, available])).toEqual([
    ["Telegram", false],
    ["Email", false],
  ]);

  await page.goto("/settings?tab=notifications");

  await expect(page.getByText(uk.settings.notifications.telegram.unavailable)).toBeVisible();
  await expect(page.getByText(uk.settings.notifications.email.unavailable)).toBeVisible();
  await expect(page.getByRole("button", { name: uk.settings.notifications.telegram.connect })).toHaveCount(0);
  await expect(page.getByRole("button", { name: uk.settings.notifications.email.add })).toHaveCount(0);
});
