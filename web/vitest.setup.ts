import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach, vi } from "vitest";
import { delegatingFetch, resetFetchStub } from "./src/test/fetch-stub";
import { navigationModule, resetNavigation } from "./src/test/navigation";
import { resetTimers } from "./src/test/timers";

// The owner's days are Europe/Kyiv days. A runner in any other zone (a CI machine in UTC, a laptop
// elsewhere) must not be able to hide a date that shifts with the zone, so the tests run far from Kyiv.
process.env.TZ = "America/Los_Angeles";

// The api client takes `fetch` once, when it is created, so the stub has to be in place before any test
// file imports it. The delegate answers from the routes a test sets with stubFetch.
vi.stubGlobal("fetch", delegatingFetch);

// A relative url such as "/api/periods/2026" is valid in a browser and not in Node's Request.
const NodeRequest = globalThis.Request;
vi.stubGlobal(
  "Request",
  class extends NodeRequest {
    constructor(input: RequestInfo | URL, init?: RequestInit) {
      super(typeof input === "string" && input.startsWith("/") ? new URL(input, location.href) : input, init);
    }
  },
);

vi.mock("next/navigation", () => navigationModule);

afterEach(() => {
  cleanup();
  resetTimers();
  resetNavigation();
  resetFetchStub();
});
