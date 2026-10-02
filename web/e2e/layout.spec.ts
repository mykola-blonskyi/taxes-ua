import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { expect, test, type Page } from "@playwright/test";
import ru from "../messages/ru.json";
import uk from "../messages/uk.json";
import { removeMonobankToken, seedRejectedMonobankToken, seedScreensWithContent } from "./support/layout-seed";

// Every screen at phone width, in both languages. A page that scrolls sideways, loses its disclaimer or
// renders the wrong language fails here, and the failure names the route, the language and the element.

const catalogs = { uk, ru };
const locales = ["uk", "ru"] as const;
const viewport = { width: 375, height: 812 };

// Strips that scroll on their own by design, named by the `data-scroll-strip` attribute the component
// carries, never by its shape. A strip that scrolls and is not listed for its route (and tab, when given)
// is a failure, so a table that starts overflowing its card cannot hide behind its own scrollbar.
const scrollingStrips: readonly { route: string; tab?: string; strip: string; reason: string }[] = [
  { route: "/settings", strip: "settings-tabs", reason: "nine settings tabs do not fit 375 px, so the tab list scrolls" },
];

// Text that proves a screen painted its seeded rows and not an empty state or a loading line. It is filled
// once the seed has run, because the seed picks names that are new on every run.
const seededText: Record<string, string[]> = {};

// A route with a dynamic segment needs a concrete address before it can be opened.
const dynamicRouteAddresses: Record<string, string> = {};

const appDir = path.resolve(__dirname, "../src/app");

function discoverRoutes() {
  const found: string[][] = [];
  const walk = (dir: string, segments: string[]) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      if (entry.isDirectory()) {
        const group = entry.name.startsWith("(") && entry.name.endsWith(")");
        walk(path.join(dir, entry.name), group ? segments : [...segments, entry.name]);
      } else if (/^page\.(tsx|ts|jsx|js)$/.test(entry.name)) {
        found.push(segments);
      }
    }
  };
  walk(appDir, []);

  const routes = found.map((segments) => `/${segments.join("/")}`.replace(/\/$/, "") || "/").sort();

  // A navigation link with no page behind it would be measured as a 404 and look like a missing screen.
  const navigation = readFileSync(path.resolve(__dirname, "../src/shared/constants/navigation.ts"), "utf8");
  const linked = [...navigation.matchAll(/href:\s*"([^"]+)"/g)].map((match) => match[1]);
  const missing = linked.filter((href) => !routes.includes(href));
  if (missing.length > 0) {
    throw new Error(`navigation.ts links routes with no page.tsx: ${missing.join(", ")}`);
  }

  return routes;
}

const routes = discoverRoutes();

// Every catalog line under a key that matches. While a "loading" line is on screen a panel has not painted
// its data yet, and measuring it would call a loading line clean.
function catalogPattern(locale: (typeof locales)[number], keyPattern: RegExp) {
  const lines: string[] = [];
  const walk = (node: unknown, key: string) => {
    if (typeof node === "string") {
      if (keyPattern.test(key)) lines.push(node);
    } else if (node && typeof node === "object") {
      for (const [name, child] of Object.entries(node)) walk(child, name);
    }
  };
  walk(catalogs[locale], "");
  return new RegExp(lines.map((line) => line.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")).join("|"));
}

// The screens fetch after they paint, so the heading alone is too early to measure. A failed fetch leaves
// an error line and no data, which would measure clean, so it fails the check instead.
async function settled(page: Page, locale: (typeof locales)[number]) {
  await expect.poll(() => page.locator("main").innerText(), { message: "a loading line is still on screen" }).not.toMatch(
    catalogPattern(locale, /^loading/),
  );
  await page.waitForLoadState("networkidle", { timeout: 10_000 });
  await expect(page.locator("main"), "a screen reports that its data failed to load").not.toContainText(
    catalogPattern(locale, /^loadFailed/),
  );
}

type Finding = { kind: "overflow" | "strip"; element: string; detail: string };
type Measurement = {
  scrollWidth: number;
  clientWidth: number;
  lang: string;
  bodyText: string;
  findings: Finding[];
};

// Runs in the page. An element is an offender when it reaches past the viewport and no ancestor clips it
// inside the viewport. A scroll container that is wider than its box is a strip, and the allow-list decides
// whether it may be one.
function measure(allowedStrips: string[]): Measurement {
  const doc = document.documentElement;
  const clientWidth = doc.clientWidth;

  const describe = (element: Element) => {
    const classes = typeof element.className === "string" ? element.className.trim().replace(/\s+/g, ".").slice(0, 80) : "";
    const label = element.getAttribute("aria-label");
    const text = (element.textContent ?? "").trim().replace(/\s+/g, " ").slice(0, 40);
    return `<${element.tagName.toLowerCase()}${classes ? `.${classes}` : ""}${label ? ` aria-label="${label}"` : ""}> "${text}"`;
  };

  // Only a scroll container holds its children: a box with overflow hidden or clip cuts them off, which
  // leaves the content unreadable and the page clean.
  const scrolls = (element: Element) => {
    const { overflowX } = getComputedStyle(element);
    return overflowX === "auto" || overflowX === "scroll";
  };

  const findings: Finding[] = [];
  for (const element of document.querySelectorAll("body *")) {
    const box = element.getBoundingClientRect();
    if (box.width === 0 && box.height === 0) continue;

    const style = getComputedStyle(element);
    const wider = element.scrollWidth > element.clientWidth + 1;
    if (scrolls(element) && wider) {
      if (!allowedStrips.includes((element as HTMLElement).dataset.scrollStrip ?? "")) {
        findings.push({
          kind: "strip",
          element: describe(element),
          detail: `scrolls on its own: scrollWidth ${element.scrollWidth}, clientWidth ${element.clientWidth}`,
        });
      }
    } else if (
      (style.overflowX === "hidden" || style.overflowX === "clip") &&
      wider &&
      style.textOverflow !== "ellipsis" &&
      // A 1 px box is an sr-only label, hidden on purpose.
      element.clientWidth > 1 &&
      // A field scrolls its own value inside itself.
      !["INPUT", "TEXTAREA", "SELECT"].includes(element.tagName)
    ) {
      findings.push({
        kind: "overflow",
        element: describe(element),
        detail: `clips its content: scrollWidth ${element.scrollWidth}, clientWidth ${element.clientWidth}`,
      });
    }

    if (box.right > clientWidth + 0.5 || box.left < -0.5) {
      let ancestor = element.parentElement;
      let contained = false;
      while (ancestor && ancestor !== doc) {
        const parentBox = ancestor.getBoundingClientRect();
        if (scrolls(ancestor) && parentBox.right <= clientWidth + 0.5 && parentBox.left >= -0.5) {
          contained = true;
          break;
        }
        ancestor = ancestor.parentElement;
      }
      if (!contained) {
        findings.push({
          kind: "overflow",
          element: describe(element),
          detail: `spans ${Math.round(box.left)}..${Math.round(box.right)} of a ${clientWidth} px viewport`,
        });
      }
    }
  }

  return {
    scrollWidth: doc.scrollWidth,
    clientWidth,
    lang: doc.lang,
    bodyText: document.body.innerText,
    findings: findings.slice(0, 8),
  };
}

async function inspect(page: Page, route: string, locale: (typeof locales)[number], state: string) {
  const where = `${route}${state ? ` (${state})` : ""} in ${locale}`;
  const tab = await page.evaluate(() => document.querySelector('[role="tab"][aria-selected="true"]')?.id.split("-trigger-")[1]);
  const allowed = scrollingStrips
    .filter((entry) => entry.route === route && (entry.tab === undefined || entry.tab === tab))
    .map((entry) => entry.strip);
  const measured = await page.evaluate(measure, allowed);
  const problems: string[] = [];

  if (measured.scrollWidth > measured.clientWidth) {
    problems.push(`${where}: the page scrolls sideways, scrollWidth ${measured.scrollWidth} > clientWidth ${measured.clientWidth}`);
  }
  for (const finding of measured.findings) {
    problems.push(`${where}: ${finding.element} ${finding.detail}`);
  }
  if (!measured.bodyText.includes(catalogs[locale].disclaimer)) {
    problems.push(`${where}: the ${locale} disclaimer is not in the rendered text`);
  }
  if (measured.lang !== locale) {
    problems.push(`${where}: html lang is "${measured.lang}", expected "${locale}"`);
  }
  return problems;
}

async function open(page: Page, route: string, locale: (typeof locales)[number]) {
  await page.goto(dynamicRouteAddresses[route] ?? route);
  await expect(page.locator("main h2").first()).not.toBeEmpty();
  for (const text of seededText[route] ?? []) {
    await expect(page.getByText(text).first(), `${route} should show the seeded "${text}"`).toBeVisible();
  }
  await settled(page, locale);
}

test.beforeAll(async ({ playwright }, testInfo) => {
  const { baseURL, storageState } = testInfo.project.use;
  const owner = await playwright.request.newContext({ baseURL, storageState });
  try {
    const { longClientName, longForeignClientName, paymentNote } = await seedScreensWithContent(owner);
    seededText["/transactions"] = [longClientName];
    seededText["/payments"] = [paymentNote];
    seededText["/invoices"] = [longForeignClientName];
    // The dashboard sync card is measured in a problem state, not only as the quiet line.
    await seedRejectedMonobankToken(owner);
  } finally {
    await owner.dispose();
  }
});

test.afterAll(async ({ playwright }, testInfo) => {
  const { baseURL, storageState } = testInfo.project.use;
  const owner = await playwright.request.newContext({ baseURL, storageState });
  try {
    await removeMonobankToken(owner);
  } finally {
    await owner.dispose();
  }
});

for (const locale of locales) {
  test.describe(`at 375 px in ${locale}`, () => {
    // The old script emulated a touch phone below 768 px, and the app may branch on it.
    test.use({ viewport, isMobile: true, hasTouch: true });

    test.beforeEach(async ({ context, baseURL }) => {
      await context.addCookies([{ name: "locale", value: locale, url: baseURL! }]);
    });

    for (const route of routes) {
      if (route === "/login") {
        continue;
      }
      if (route.includes("[") && !(route in dynamicRouteAddresses)) {
        test(`${route} has a concrete address`, () => {
          throw new Error(`${route} has a dynamic segment; add an address for it to dynamicRouteAddresses in layout.spec.ts`);
        });
        continue;
      }

      test(`${route} does not scroll sideways`, async ({ page }) => {
        // A tabbed route opens nine panels, each waiting for its data.
        test.setTimeout(60_000);
        await open(page, route, locale);
        if (route === "/") {
          await expect(
            page.getByRole("alert").filter({ hasText: catalogs[locale].dashboard.sync.tokenRejected.title }),
            "the dashboard should show the rejected-token card",
          ).toBeVisible();
        }
        const problems = await inspect(page, route, locale, "");

        // Each tab is a state of the route, and a panel behind a tab the first render never shows can
        // overflow on its own.
        const tabs = page.getByRole("tab");
        const count = await tabs.count();
        for (let index = 1; index < count; index++) {
          const tab = tabs.nth(index);
          const name = (await tab.innerText()).trim();
          await tab.click();
          await expect(tab).toHaveAttribute("aria-selected", "true");
          await settled(page, locale);
          problems.push(...(await inspect(page, route, locale, `tab ${index + 1} "${name}"`)));
        }

        expect(problems, problems.join("\n")).toEqual([]);
      });
    }

    test.describe("signed out", () => {
      test.use({ storageState: { cookies: [], origins: [] } });

      test("/login does not scroll sideways", async ({ page, context, baseURL }) => {
        await context.addCookies([{ name: "locale", value: locale, url: baseURL! }]);
        await open(page, "/login", locale);
        const problems = await inspect(page, "/login", locale, "");

        expect(problems, problems.join("\n")).toEqual([]);
      });
    });
  });
}
