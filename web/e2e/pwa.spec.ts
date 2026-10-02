import { createServer } from "node:http";
import type { AddressInfo } from "node:net";
import { expect, test, type Page } from "@playwright/test";
import ru from "../messages/ru.json";
import uk from "../messages/uk.json";

// The worker is registered after load and takes over its own page once it activates, which is after the
// precache has finished. Everything below needs that, so wait for the controller, not for a timer.
async function workerControls(page: Page) {
  await page.waitForFunction(() => navigator.serviceWorker.controller !== null);
}

test("offline, a navigation shows the fallback page in the owner's language, never tax data", async ({ page, context, baseURL }) => {
  await page.goto("/");
  await expect(page.getByRole("heading", { level: 2, name: uk.dashboard.title })).toBeVisible();
  await workerControls(page);

  await context.setOffline(true);
  await page.goto("/payments");

  await expect(page.getByRole("heading", { name: uk.offline.title })).toBeVisible();
  await expect(page.getByText(uk.offline.body)).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute("lang", "uk");
  await expect(page.getByRole("navigation", { name: uk.nav.label })).toHaveCount(0);

  await context.addCookies([{ name: "locale", value: "ru", url: baseURL! }]);
  await page.goto("/settings");
  await expect(page.getByRole("heading", { name: ru.offline.title })).toBeVisible();
  await expect(page.getByText(ru.offline.body)).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute("lang", "ru");

  await context.setOffline(false);
  await page.getByRole("button", { name: ru.offline.retry }).click();
  await expect(page.getByRole("heading", { level: 2, name: ru.settings.title })).toBeVisible();
});

test("the service worker caches static files and the fallback, never an api response or a page", async ({ page }) => {
  await page.goto("/transactions");
  await expect(page.getByRole("heading", { level: 2, name: uk.transactions.title })).toBeVisible();
  await workerControls(page);
  await page.waitForLoadState("networkidle");

  const cached = await page.evaluate(async () => {
    const urls: string[] = [];
    for (const name of await caches.keys()) {
      for (const request of await (await caches.open(name)).keys()) {
        urls.push(new URL(request.url).pathname);
      }
    }
    return urls;
  });

  expect(cached).toContain("/offline.html");
  expect(cached.some((url) => url.startsWith("/_next/static/"))).toBe(true);
  expect(cached.filter((url) => url.startsWith("/api/"))).toEqual([]);
  expect(cached.filter((url) => ["/", "/transactions", "/payments", "/settings"].includes(url))).toEqual([]);
});

// A deploy is a worker script whose bytes differ, and Playwright cannot rewrite the browser's own update
// check for sw.js. So the page is loaded through a thin proxy to the real stack that can serve a changed
// sw.js on demand. The session cookie is shared across ports on localhost.
async function proxyToStack(upstream: string) {
  let suffix = "";
  const server = createServer(async (incoming, outgoing) => {
    const headers = new Headers();
    for (const [name, value] of Object.entries(incoming.headers)) {
      // No conditional request, so the worker script always comes back whole and can be extended.
      if (!["host", "if-none-match", "if-modified-since"].includes(name) && typeof value === "string") headers.set(name, value);
    }
    const chunks: Buffer[] = [];
    for await (const chunk of incoming) chunks.push(chunk as Buffer);
    const response = await fetch(new URL(incoming.url ?? "/", upstream), {
      method: incoming.method,
      headers,
      body: chunks.length > 0 ? Buffer.concat(chunks) : undefined,
      redirect: "manual",
    });
    const body = Buffer.from(await response.arrayBuffer());
    const forwarded = incoming.url === "/sw.js" ? Buffer.concat([body, Buffer.from(suffix)]) : body;
    const responseHeaders: Record<string, string | string[]> = {};
    response.headers.forEach((value, name) => {
      if (!["content-encoding", "content-length", "transfer-encoding", "set-cookie"].includes(name)) {
        responseHeaders[name] = value;
      }
    });
    responseHeaders["set-cookie"] = response.headers.getSetCookie();
    outgoing.writeHead(response.status, responseHeaders).end(forwarded);
  });
  await new Promise<void>((resolve) => server.listen(0, resolve));
  const { port } = server.address() as AddressInfo;

  return {
    url: `http://localhost:${port}`,
    deploy: () => {
      suffix = `\n// next deploy ${Date.now()}`;
    },
    close: () =>
      new Promise((resolve) => {
        server.close(resolve);
        server.closeAllConnections();
      }),
  };
}

test("a new version waits behind a prompt, and the owner's tab reloads only when they accept", async ({ page, baseURL }) => {
  const stack = await proxyToStack(baseURL!);
  try {
    await page.goto(`${stack.url}/`);
    await expect(page.getByRole("heading", { level: 2, name: uk.dashboard.title })).toBeVisible();
    await workerControls(page);
    await expect(page.getByText(uk.pwa.updateAvailable)).toHaveCount(0);

    stack.deploy();
    await page.evaluate(async () => {
      (window as unknown as { marker: string }).marker = "before";
      const registration = await navigator.serviceWorker.ready;
      await registration.update();
    });

    const prompt = page.getByRole("status").filter({ hasText: uk.pwa.updateAvailable });
    await expect(prompt).toBeVisible();
    // Nothing reloaded the page under the owner.
    expect(await page.evaluate(() => (window as unknown as { marker?: string }).marker)).toBe("before");

    await Promise.all([page.waitForEvent("load"), prompt.getByRole("button", { name: uk.pwa.reload }).click()]);

    expect(await page.evaluate(() => (window as unknown as { marker?: string }).marker)).toBeUndefined();
    await expect(page.getByRole("heading", { level: 2, name: uk.dashboard.title })).toBeVisible();
    await expect(page.getByText(uk.pwa.updateAvailable)).toHaveCount(0);
  } finally {
    await stack.close();
  }
});

test("a second tab still reloads after the first tab accepted the update", async ({ page, context, baseURL }) => {
  const stack = await proxyToStack(baseURL!);
  try {
    const marker = () => (window as unknown as { marker?: string }).marker;
    const second = await context.newPage();
    await page.goto(`${stack.url}/`);
    await workerControls(page);
    await second.goto(`${stack.url}/payments`);
    await workerControls(second);

    stack.deploy();
    await page.evaluate(async () => {
      (window as unknown as { marker: string }).marker = "first";
      await (await navigator.serviceWorker.ready).update();
    });
    await second.evaluate(() => {
      (window as unknown as { marker: string }).marker = "second";
    });
    const promptOf = (target: Page) => target.getByRole("status").filter({ hasText: uk.pwa.updateAvailable });
    await expect(promptOf(page)).toBeVisible();
    await expect(promptOf(second)).toBeVisible();

    await Promise.all([page.waitForEvent("load"), promptOf(page).getByRole("button", { name: uk.pwa.reload }).click()]);
    // The worker is active now, so nothing is waiting: the second tab's button must reload by itself.
    await Promise.all([second.waitForEvent("load"), promptOf(second).getByRole("button", { name: uk.pwa.reload }).click()]);

    expect(await second.evaluate(marker)).toBeUndefined();
    await expect(second.getByRole("heading", { level: 2, name: uk.payments.title })).toBeVisible();
  } finally {
    await stack.close();
  }
});

test("each screen has its own localized tab title", async ({ page, context, baseURL }) => {
  await page.goto("/payments");
  await expect(page).toHaveTitle(`${uk.payments.title} · ${uk.app.title}`);
  await page.goto("/settings");
  await expect(page).toHaveTitle(`${uk.settings.title} · ${uk.app.title}`);

  await context.addCookies([{ name: "locale", value: "ru", url: baseURL! }]);
  await page.goto("/payments");
  await expect(page).toHaveTitle(`${ru.payments.title} · ${ru.app.title}`);
});

test("the manifest is installable: id, split any and maskable icons, one theme", async ({ request }) => {
  const manifest = await (await request.get("/manifest.json")).json();

  expect(manifest.id).toBe("/");
  expect(manifest.theme_color).toBe(manifest.background_color);
  const purposes = (manifest.icons as { sizes: string; purpose: string }[]).map((icon) => `${icon.sizes} ${icon.purpose}`);
  expect(purposes).toEqual(expect.arrayContaining(["192x192 any", "512x512 any", "192x192 maskable", "512x512 maskable"]));
  expect(purposes.some((purpose) => purpose.includes("any maskable"))).toBe(false);
});
