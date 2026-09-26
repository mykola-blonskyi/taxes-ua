#!/usr/bin/env node
import { spawn } from "node:child_process";
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../../../..");

const options = {
  base: "http://localhost:3000",
  email: "owner@example.com",
  width: 375,
  height: 812,
  locale: "uk",
  theme: "light",
  out: join(repoRoot, ".verify"),
  port: 9222,
  timeout: 20000,
};

for (let index = 2; index < process.argv.length; index += 2) {
  const key = process.argv[index].replace(/^--/, "");
  if (!(key in options)) {
    throw new Error(`unknown option --${key}. Known: ${Object.keys(options).join(", ")}`);
  }
  const value = process.argv[index + 1];
  options[key] = typeof options[key] === "number" ? Number(value) : value;
}

function discoverRoutes() {
  const appDir = join(repoRoot, "web/src/app");
  const found = [];

  const walk = (dir, segments) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      if (entry.isDirectory()) {
        const group = entry.name.startsWith("(") && entry.name.endsWith(")");
        walk(join(dir, entry.name), group ? segments : [...segments, entry.name]);
      } else if (/^page\.(tsx|ts|jsx|js)$/.test(entry.name)) {
        found.push(segments);
      }
    }
  };

  walk(appDir, []);

  const routes = [];
  for (const segments of found) {
    const dynamic = segments.find((segment) => segment.includes("["));
    const path = `/${segments.join("/")}`.replace(/\/$/, "") || "/";
    if (dynamic) {
      routes.push({ path, skip: `dynamic segment ${dynamic} needs seeded data` });
    } else {
      routes.push({ path });
    }
  }

  const navPath = join(repoRoot, "web/src/shared/constants/navigation.ts");
  const navHrefs = [...readFileSync(navPath, "utf8").matchAll(/href:\s*"([^"]+)"/g)].map((m) => m[1]);
  const missing = navHrefs.filter((href) => !routes.some((route) => route.path === href));
  if (missing.length > 0) {
    throw new Error(`navigation.ts links routes with no page.tsx: ${missing.join(", ")}`);
  }

  return routes.sort((a, b) => a.path.localeCompare(b.path));
}

function chromeCommand() {
  const candidates = [
    process.env.CHROME_PATH,
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    "/Applications/Chromium.app/Contents/MacOS/Chromium",
    "/usr/bin/google-chrome",
    "/usr/bin/chromium",
    "/usr/bin/chromium-browser",
  ].filter(Boolean);

  for (const candidate of candidates) {
    try {
      readFileSync(candidate);
      return candidate;
    } catch {
      continue;
    }
  }

  throw new Error(
    `no Chrome binary found. Tried:\n  ${candidates.join("\n  ")}\nSet CHROME_PATH to one.`,
  );
}

async function waitFor(describe, predicate, timeout = options.timeout) {
  const deadline = Date.now() + timeout;
  let last;
  while (Date.now() < deadline) {
    try {
      last = await predicate();
      if (last) {
        return last;
      }
    } catch (error) {
      last = error.message;
    }
    await new Promise((done) => setTimeout(done, 150));
  }
  throw new Error(`timed out after ${timeout}ms waiting for ${describe}. Last: ${last}`);
}

async function connect(webSocketDebuggerUrl) {
  const socket = new WebSocket(webSocketDebuggerUrl);
  await new Promise((done, fail) => {
    socket.addEventListener("open", done, { once: true });
    socket.addEventListener("error", () => fail(new Error("CDP socket failed")), { once: true });
  });

  let nextId = 1;
  const pending = new Map();

  socket.addEventListener("message", (event) => {
    const message = JSON.parse(event.data);
    const settle = pending.get(message.id);
    if (!settle) {
      return;
    }
    pending.delete(message.id);
    if (message.error) {
      settle.fail(new Error(`${message.error.message} (${message.error.code})`));
    } else {
      settle.done(message.result);
    }
  });

  return {
    send(method, params = {}) {
      const id = nextId++;
      socket.send(JSON.stringify({ id, method, params }));
      return new Promise((done, fail) => pending.set(id, { done, fail }));
    },
    close: () => socket.close(),
  };
}

async function evaluate(page, expression) {
  const { result, exceptionDetails } = await page.send("Runtime.evaluate", {
    expression,
    returnByValue: true,
    awaitPromise: true,
  });
  if (exceptionDetails) {
    throw new Error(exceptionDetails.exception?.description ?? exceptionDetails.text);
  }
  return result.value;
}

const measureExpression = (disclaimer) => `(() => {
  const doc = document.documentElement;
  const clientWidth = doc.clientWidth;
  const nav = document.querySelector("nav");
  const offenders = [];
  for (const element of document.querySelectorAll("body *")) {
    const box = element.getBoundingClientRect();
    if (box.width === 0 && box.height === 0) continue;
    if (box.right > clientWidth + 0.5 || box.left < -0.5) {
      offenders.push({
        tag: element.tagName.toLowerCase(),
        classes: typeof element.className === "string" ? element.className.slice(0, 100) : "",
        left: Math.round(box.left),
        right: Math.round(box.right),
        text: (element.textContent || "").trim().slice(0, 40),
      });
    }
  }
  return {
    path: location.pathname,
    scrollWidth: doc.scrollWidth,
    clientWidth,
    bodyScrollWidth: document.body.scrollWidth,
    hasDisclaimer: document.body.innerText.includes(${JSON.stringify(disclaimer)}),
    hasNav: nav !== null,
    navLabel: nav && nav.getAttribute("aria-label"),
    navWidth: nav && Math.round(nav.getBoundingClientRect().width),
    htmlLang: doc.lang,
    themeClass: doc.className.split(/\\s+/).find((name) => name === "dark" || name === "light") || "(none)",
    bodyBackground: getComputedStyle(document.body).backgroundColor,
    offenders: offenders.slice(0, 6),
  };
})()`;

const routes = discoverRoutes();
const messages = JSON.parse(readFileSync(join(repoRoot, `web/messages/${options.locale}.json`), "utf8"));
const disclaimer = messages.disclaimer;

mkdirSync(options.out, { recursive: true });
const profile = mkdtempSync(join(tmpdir(), "verify-taxes-ua-"));

const chrome = spawn(
  chromeCommand(),
  [
    "--headless=new",
    `--remote-debugging-port=${options.port}`,
    `--user-data-dir=${profile}`,
    "--no-first-run",
    "--no-default-browser-check",
    "--disable-extensions",
    "about:blank",
  ],
  { stdio: "ignore" },
);

const results = [];
const failures = [];
let page;

try {
  const target = await waitFor("Chrome's debugging port", async () => {
    const response = await fetch(`http://127.0.0.1:${options.port}/json/list`);
    return (await response.json()).find((entry) => entry.type === "page");
  });

  page = await connect(target.webSocketDebuggerUrl);
  await page.send("Page.enable");
  await page.send("Runtime.enable");
  await page.send("Network.enable");
  await page.send("Emulation.setEmulatedMedia", {
    features: [{ name: "prefers-color-scheme", value: options.theme }],
  });
  await page.send("Network.setCookie", {
    name: "locale",
    value: options.locale,
    url: options.base,
  });

  const seam = `${options.base}/api/auth/login/development?email=${encodeURIComponent(options.email)}&returnUrl=%2F`;
  await page.send("Page.navigate", { url: seam });
  await waitFor(
    "the Development sign-in seam to mint a session",
    async () => (await evaluate(page, `fetch("/api/auth/me").then((r) => r.status)`)) === 200,
  );

  await page.send("Emulation.setDeviceMetricsOverride", {
    width: options.width,
    height: options.height,
    deviceScaleFactor: 1,
    mobile: options.width < 768,
  });

  for (const route of routes) {
    if (route.skip) {
      results.push({ path: route.path, skipped: route.skip });
      continue;
    }

    await page.send("Page.navigate", { url: `${options.base}${route.path}` });

    let measurement;
    try {
      await waitFor(
        `rendered content on ${route.path}`,
        async () =>
          await evaluate(
            page,
            `!!document.querySelector("main h2") && document.querySelector("main h2").textContent.trim().length > 0`,
          ),
      );
      measurement = await evaluate(page, measureExpression(disclaimer));
    } catch (error) {
      failures.push(`${route.path}: ${error.message}`);
      results.push({ path: route.path, error: error.message });
      continue;
    }

    const slug = route.path === "/" ? "root" : route.path.replace(/^\//, "").replace(/\//g, "-");
    const file = `${options.width}px-${slug}-${options.locale}-${options.theme}.png`;
    const shot = await page.send("Page.captureScreenshot", {
      format: "png",
      captureBeyondViewport: true,
    });
    writeFileSync(join(options.out, file), Buffer.from(shot.data, "base64"));

    if (measurement.scrollWidth > measurement.clientWidth) {
      failures.push(
        `${route.path}: scrollWidth ${measurement.scrollWidth} exceeds clientWidth ${measurement.clientWidth}`,
      );
    }
    if (!measurement.hasDisclaimer) {
      failures.push(`${route.path}: the ${options.locale} disclaimer is not in the rendered text`);
    }
    if (measurement.htmlLang !== options.locale) {
      failures.push(`${route.path}: html lang is ${measurement.htmlLang}, expected ${options.locale}`);
    }

    results.push({ ...measurement, screenshot: file });
  }
} finally {
  page?.close();
  chrome.kill("SIGTERM");
}

const report = {
  base: options.base,
  viewport: `${options.width}x${options.height}`,
  locale: options.locale,
  theme: options.theme,
  routes: results,
  failures,
};
const reportFile = join(options.out, `report-${options.width}px-${options.locale}-${options.theme}.json`);
writeFileSync(reportFile, `${JSON.stringify(report, null, 2)}\n`);

for (const row of results) {
  if (row.skipped) {
    console.log(`${row.path.padEnd(14)} skipped: ${row.skipped}`);
  } else if (row.error) {
    console.log(`${row.path.padEnd(14)} FAILED: ${row.error}`);
  } else {
    console.log(
      [
        row.path.padEnd(14),
        `scrollWidth ${String(row.scrollWidth).padStart(5)}`,
        `clientWidth ${String(row.clientWidth).padStart(5)}`,
        `nav ${row.hasNav ? `${row.navWidth}px` : "absent"}`.padEnd(12),
        `disclaimer ${row.hasDisclaimer ? "yes" : "NO"}`,
        `lang ${row.htmlLang}`,
        `theme ${row.themeClass}`,
        row.screenshot,
      ].join("  "),
    );
  }
  for (const offender of row.offenders ?? []) {
    console.log(`  overflows: <${offender.tag} class="${offender.classes}"> right=${offender.right} "${offender.text}"`);
  }
}

console.log(`\nreport: ${reportFile}`);

if (failures.length > 0) {
  console.error(`\n${failures.length} failure(s):`);
  for (const failure of failures) {
    console.error(`  ${failure}`);
  }
  process.exit(1);
}

console.log(`${results.filter((row) => !row.skipped).length} route(s) measured, no overflow`);
