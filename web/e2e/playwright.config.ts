import { execFileSync } from "node:child_process";
import path from "node:path";
import { defineConfig, devices } from "@playwright/test";

// The config loads in the runner and again in every worker, so the port is picked once and handed down
// through the environment. The operating system hands out a free ephemeral port, which is never 3000.
process.env.E2E_WEB_PORT ??= execFileSync(
  process.execPath,
  ["-e", "const s = require('net').createServer().listen(0, () => { console.log(s.address().port); s.close(); });"],
  { encoding: "utf8" },
).trim();
const baseURL = `http://localhost:${process.env.E2E_WEB_PORT}`;

export default defineConfig({
  testDir: __dirname,
  testMatch: "**/*.spec.ts",
  globalSetup: "./support/global-setup.ts",
  outputDir: path.join(__dirname, "test-results"),
  // One browser against one shared database. Each test seeds and asserts on its own data, so the
  // suite does not depend on order; it simply is not parallel.
  workers: 1,
  // A retry would hide a flaky test behind a green run.
  retries: 0,
  // Covers the image builds in global setup as well as the tests. It stays under the CI job's 15 minutes,
  // so a hung run ends here with a report and traces instead of being cancelled without them.
  globalTimeout: 12 * 60_000,
  timeout: 30_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI
    ? [["list"], ["html", { open: "never", outputFolder: path.join(__dirname, "playwright-report") }]]
    : [["list"]],
  use: {
    baseURL,
    storageState: path.join(__dirname, ".auth", "owner.json"),
    locale: "uk-UA",
    timezoneId: "Europe/Kyiv",
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
