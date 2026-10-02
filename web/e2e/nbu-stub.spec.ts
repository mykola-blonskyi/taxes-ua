import { expect, test } from "@playwright/test";

// A rate can only come from the stub the stack points at, so no flow in the suite can reach the real NBU.
test("exchange rates are served by the NBU stub, not the real NBU", async ({ request }) => {
  const response = await request.get("/api/fx?currency=USD&date=2026-03-02");

  expect(response.status()).toBe(200);
  expect(await response.json()).toMatchObject({ currency: "USD", date: "2026-03-02", rateE4: 400000 });
});
