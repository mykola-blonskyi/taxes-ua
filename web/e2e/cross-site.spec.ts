import { expect, test } from "@playwright/test";

// ADR-024: the api refuses an unsafe request the browser says came from another origin. The headers
// have to survive the Next rewrite for that to work, which only the real stack can show. A real browser
// cannot be made to send a cross-site request here: Chromium blocks loopback to loopback across origins
// before anything is sent, so the browser's headers are stood in for by setting them on the request.
const rotateFeed = "/api/calendar/feed/rotate";

test.describe("cross-site requests", () => {
  test("the app's own page can still post without a body", async ({ page }) => {
    await page.goto("/login");

    const status = await page.evaluate(async (path) => (await fetch(path, { method: "POST" })).status, rotateFeed);

    expect(status).toBe(200);
  });

  test("Sec-Fetch-Site and Origin reach the api unchanged", async ({ request }) => {
    const crossSite = await request.post(rotateFeed, { headers: { "Sec-Fetch-Site": "cross-site" } });
    const foreignOrigin = await request.post(rotateFeed, { headers: { Origin: "https://todo.blonskyi.dev" } });
    const sameOrigin = await request.post(rotateFeed, { headers: { "Sec-Fetch-Site": "same-origin" } });

    expect(crossSite.status()).toBe(403);
    expect(foreignOrigin.status()).toBe(403);
    expect(sameOrigin.status()).toBe(200);
  });
});
