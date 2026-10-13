import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import ru from "../messages/ru.json";
import uk from "../messages/uk.json";
import { chooseLanguage, resetAppearance } from "./support/appearance";
import { seedTreasuryAccounts } from "./support/seed";
import { removeMonobankToken, seedRejectedMonobankToken, seedScreensWithContent } from "./support/layout-seed";

// Every screen at phone width, in both languages. A page that scrolls sideways, loses its disclaimer or
// renders the wrong language fails here, and the failure names the route, the language and the element.

const catalogs = { uk, ru };
type Locale = "uk" | "ru";
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
function catalogPattern(locale: Locale, keyPattern: RegExp) {
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
async function settled(page: Page, locale: Locale) {
  await expect.poll(() => page.locator("main").innerText(), { message: "a loading line is still on screen" }).not.toMatch(
    catalogPattern(locale, /^loading/),
  );
  await page.waitForLoadState("networkidle", { timeout: 10_000 });
  await expect(page.locator("main"), "a screen reports that its data failed to load").not.toContainText(
    catalogPattern(locale, /^loadFailed/),
  );
}

type Finding = { kind: "overflow" | "strip" | "target"; element: string; detail: string };
type Measurement = {
  scrollWidth: number;
  clientWidth: number;
  coarse: boolean;
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

  // A coarse pointer needs 44 px to hit. A control's box counts, and so does the label tied to a checkbox
  // or radio (the label is what a thumb taps). Text links inside a sentence are exempt, as in WCAG 2.5.8;
  // an element a screen hides on purpose (sr-only, hidden file inputs) is not a target.
  const coarse = matchMedia("(pointer: coarse)").matches;
  const minimum = 44;
  const targets = document.querySelectorAll(
    'button, a[href], select, textarea, summary, [role="tab"], input:not([type="hidden"])',
  );
  const targetFindings: Finding[] = [];
  for (const element of Array.from(targets)) {
    const box = element.getBoundingClientRect();
    if (box.width <= 1 || box.height <= 1) continue;
    if (element instanceof HTMLInputElement && element.type === "file") continue;
    if (element.closest("[inert], [aria-hidden='true']")) continue;

    let left = box.left;
    let right = box.right;
    let top = box.top;
    let bottom = box.bottom;
    if (element instanceof HTMLInputElement && (element.type === "checkbox" || element.type === "radio")) {
      for (const label of Array.from(element.labels ?? [])) {
        const labelBox = label.getBoundingClientRect();
        left = Math.min(left, labelBox.left);
        right = Math.max(right, labelBox.right);
        top = Math.min(top, labelBox.top);
        bottom = Math.max(bottom, labelBox.bottom);
      }
    }
    const width = right - left;
    const height = bottom - top;
    if (element instanceof HTMLAnchorElement) {
      const sentence = Array.from(element.parentElement?.childNodes ?? []).some(
        (node) => node !== element && node.nodeType === Node.TEXT_NODE && (node.textContent ?? "").trim() !== "",
      );
      if (sentence) continue;
    }

    // A text link is as wide as its words, so only its height must reach the minimum.
    const narrow = !(element instanceof HTMLAnchorElement) && Math.round(width) < minimum;
    if (Math.round(height) < minimum || narrow) {
      targetFindings.push({
        kind: "target",
        element: describe(element),
        detail: `is ${Math.round(width)}x${Math.round(height)} px, under ${minimum} px for a coarse pointer`,
      });
    }
  }

  return {
    scrollWidth: doc.scrollWidth,
    clientWidth,
    coarse,
    lang: doc.lang,
    bodyText: document.body.innerText,
    findings: findings.slice(0, 8).concat(targetFindings.slice(0, 8)),
  };
}

// axe-core against the rendered state, failing on serious and critical violations. No rule is disabled: a
// rule that cannot hold for this app would be listed here with the reason, never switched off silently.
async function accessibilityViolations(page: Page, where: string) {
  const { violations } = await new AxeBuilder({ page }).analyze();

  return violations
    .filter((violation) => violation.impact === "serious" || violation.impact === "critical")
    .map((violation) => {
      const nodes = violation.nodes.slice(0, 6).map((node) => `${node.target.join(" ")} [${node.any[0]?.message ?? ""}]`).join("; ");
      return `${where}: axe ${violation.impact} ${violation.id}: ${violation.help} (${violation.nodes.length} node(s): ${nodes})`;
    });
}

async function inspect(page: Page, route: string, locale: Locale, state: string) {
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
  if (!measured.coarse) {
    problems.push(`${where}: the browser reports a fine pointer, so the touch-target check measured nothing`);
  }
  problems.push(...(await accessibilityViolations(page, where)));
  if (!measured.bodyText.includes(catalogs[locale].disclaimer)) {
    problems.push(`${where}: the ${locale} disclaimer is not in the rendered text`);
  }
  if (measured.lang !== locale) {
    problems.push(`${where}: html lang is "${measured.lang}", expected "${locale}"`);
  }
  return problems;
}

// Screens that open something over the page or turn into a form: the pay sheet, the enlarged QR, the mark-paid
// form and the invoice editor. They are states of the route as much as a tab is, and axe and the touch check
// must see them open.
async function inspectOpenStates(page: Page, route: string, locale: Locale) {
  const text = catalogs[locale];
  const problems: string[] = [];
  const payButton = page.getByRole("button", { name: text.pay.button }).first();

  if (route === "/payments" || route === "/") {
    if (route === "/payments") {
      await expect(payButton, "/payments should show a pay button to open the sheet").toBeVisible();
    }
    if (await payButton.isVisible()) {
      await payButton.click();
      const sheet = page.getByRole("dialog");
      await expect(sheet).toBeVisible();
      // A quarter with nothing owed opens on an empty amount and no QR; an amount of its own gives the sheet one.
      await sheet.getByLabel(text.pay.amountInput).fill("100");
      // The sheet is a portal that settled() does not watch: measure it once its details and QR have loaded.
      await expect(sheet.getByText(text.pay.loading)).toHaveCount(0);
      await expect(sheet.getByText(text.pay.qrUpdating)).toHaveCount(0);
      const enlarge = sheet.getByRole("button", { name: text.pay.qrEnlarge });
      await expect(enlarge, "the seeded Treasury account should give the sheet a QR to enlarge").toBeVisible();
      problems.push(...(await inspect(page, route, locale, "pay sheet")));

      await enlarge.click();
      await expect(page.getByRole("img", { name: text.pay.qrLabel }).last()).toBeVisible();
      problems.push(...(await inspect(page, route, locale, "enlarged QR")));
      await page.keyboard.press("Escape");
      await page.keyboard.press("Escape");
      await expect(page.getByRole("dialog")).toHaveCount(0);
    }
  }

  if (route === "/") {
    const markPaid = page.getByRole("button", { name: text.dashboard.markPaid });
    if (await markPaid.isVisible()) {
      await markPaid.click();
      await expect(page.getByLabel(text.dashboard.paidOn)).toBeVisible();
      problems.push(...(await inspect(page, route, locale, "mark-paid form")));
    }
  }

  if (route === "/invoices") {
    await page.getByRole("button", { name: text.invoices.new }).click();
    await expect(page.getByRole("button", { name: text.invoices.editor.back })).toBeVisible();
    problems.push(...(await inspect(page, route, locale, "invoice editor")));
  }

  return problems;
}

async function open(page: Page, route: string, locale: Locale) {
  await page.goto(dynamicRouteAddresses[route] ?? route);
  await expect(page.locator("main h2").first()).not.toBeEmpty();
  for (const text of seededText[route] ?? []) {
    await expect(page.getByText(text).first(), `${route} should show the seeded "${text}"`).toBeVisible();
  }
  await settled(page, locale);
}

test.beforeAll(async ({ playwright }, testInfo) => {
  // The hook is capped at the 30 s test timeout, which would cut the 75 s wait for the rejected token short.
  test.setTimeout(120_000);
  const { baseURL, storageState } = testInfo.project.use;
  const owner = await playwright.request.newContext({ baseURL, storageState });
  try {
    const { longClientName, longForeignClientName, paymentNote, year } = await seedScreensWithContent(owner);
    // The pay sheet needs a Treasury account to show its details and the QR that the enlarged-QR state opens.
    await seedTreasuryAccounts(owner);
    seededText["/transactions"] = [longClientName];
    seededText["/payments"] = [paymentNote];
    seededText["/invoices"] = [longForeignClientName];
    // The seeded income and payments give the year's archive its statement and register files.
    seededText["/archive"] = [`transactions-${year}.csv`, `payments-${year}.csv`];
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
    await resetAppearance(owner);
  } finally {
    await owner.dispose();
  }
});

// Light in both languages, and dark in Ukrainian: the theme changes every colour, so contrast is measured in
// both. The system preference picks the theme, as it does for a visitor with no stored choice.
const runs = [
  { locale: "uk", scheme: "light" },
  { locale: "ru", scheme: "light" },
  { locale: "uk", scheme: "dark" },
] as const;

// The bottom nav truncates its labels with an ellipsis, which the overflow gate cannot see, so measure each
// label against its own box in both languages and both themes.
for (const locale of ["uk", "ru"] as const) {
  for (const scheme of ["light", "dark"] as const) {
    test.describe(`the bottom nav at 375 px in ${locale} in the ${scheme} theme`, () => {
      test.use({ viewport, isMobile: true, hasTouch: true, colorScheme: scheme });

      test("shows every label whole", async ({ page, context, baseURL }) => {
        await chooseLanguage(context, baseURL!, locale);
        await open(page, "/", locale);
        const links = page.getByRole("navigation", { name: catalogs[locale].nav.label }).getByRole("link");
        await expect(links).toHaveCount(6);
        const labels = await links.evaluateAll((anchors) =>
          anchors.map((anchor) => {
            const label = anchor.querySelector("span") as HTMLElement;
            return { text: label.textContent, scrollWidth: label.scrollWidth, clientWidth: label.clientWidth, height: anchor.getBoundingClientRect().height };
          }),
        );
        const truncated = labels.filter((label) => label.scrollWidth > label.clientWidth);
        expect(truncated, `bottom-nav labels cut off: ${JSON.stringify(truncated)}`).toEqual([]);
        expect(labels.filter((label) => label.height < 44), "bottom-nav targets under 44 px").toEqual([]);
      });
    });
  }
}

for (const { locale, scheme } of runs) {
  test.describe(`at 375 px in ${locale}${scheme === "dark" ? " in the dark theme" : ""}`, () => {
    // The old script emulated a touch phone below 768 px, and the app may branch on it.
    test.use({ viewport, isMobile: true, hasTouch: true, colorScheme: scheme });

    test.beforeEach(async ({ context, baseURL }) => {
      await chooseLanguage(context, baseURL!, locale);
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
        await expect(page.locator("html"), `the ${scheme} theme should be the rendered one`).toHaveClass(
          scheme === "dark" ? /\bdark\b/ : /^(?!.*\bdark\b)/,
        );
        if (route === "/") {
          const rejected = page.getByRole("alert").filter({ hasText: catalogs[locale].dashboard.sync.tokenRejected.title });
          const fold = page.locator("main details > summary");
          // A rejected token leads the dashboard, but a declaration or a group 3 deadline within three days
          // goes above it and folds the warning, which depends on the date the suite runs. Open the fold then.
          await expect(rejected.or(fold).first()).toBeVisible();
          const dashboard = (await (await page.request.get("/api/dashboard")).json()) as {
            declaration: { daysLeft: number | string } | null;
            group3: { confirmed: boolean; group3Start: string | null; applicationDaysLeft: number | string | null };
          };
          const urgent =
            (dashboard.declaration !== null && Number(dashboard.declaration.daysLeft) <= 3) ||
            (!dashboard.group3.confirmed &&
              dashboard.group3.group3Start !== null &&
              dashboard.group3.applicationDaysLeft !== null &&
              Number(dashboard.group3.applicationDaysLeft) <= 3);
          if (!urgent) {
            // Nothing is within three days, so the rejected token is the one banner: visible without opening
            // anything (a role query skips what a closed <details> hides, so visibility alone proves it is
            // not folded), absent from the folded list's markup, and laid out above the pay card.
            await expect(rejected, "the rejected token should be a visible banner").toBeVisible();
            await expect(
              page.locator("main details [role=alert]").filter({ hasText: catalogs[locale].dashboard.sync.tokenRejected.title }),
              "the rejected token should not be inside the folded list",
            ).toHaveCount(0);
            const banner = await rejected.boundingBox();
            const payCard = await page.locator("#next-step-kinds").boundingBox();
            expect(banner, "the rejected-token banner has a box").not.toBeNull();
            expect(payCard, "the seeded dashboard shows the pay card").not.toBeNull();
            expect(banner!.y + banner!.height, "the rejected token should sit above the pay card").toBeLessThanOrEqual(payCard!.y);
          }
          if (await fold.isVisible()) {
            await fold.click();
          }
          await expect(
            rejected,
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

        problems.push(...(await inspectOpenStates(page, route, locale)));

        expect(problems, problems.join("\n")).toEqual([]);
      });
    }

    test.describe("signed out", () => {
      test.use({ storageState: { cookies: [], origins: [] } });

      test("/login does not scroll sideways", async ({ page, context, baseURL }) => {
        await chooseLanguage(context, baseURL!, locale);
        await open(page, "/login", locale);
        const problems = await inspect(page, "/login", locale, "");

        expect(problems, problems.join("\n")).toEqual([]);
      });
    });
  });
}
