import { randomInt } from "node:crypto";
import path from "node:path";
import { defineConfig, devices } from "@playwright/test";

// The config loads in the runner and again in every worker, so the port is picked once and handed down
// through the environment. 3000 is the developer's own stack and stays out of the range.
process.env.E2E_WEB_PORT ??= String(randomInt(40000, 50000));
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
  // Covers the image builds in global setup as well as the tests.
  globalTimeout: 20 * 60_000,
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
