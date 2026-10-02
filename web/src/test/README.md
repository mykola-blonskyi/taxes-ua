# Component test harness

The shared setup for component tests (ADR-020, layer 2). Only `*.test.ts(x)` files import it; eslint
rejects an import from anywhere else. Run the tests with `pnpm test` in `web/`.

## Writing a component test

Put `Component.test.tsx` next to the component, import from `@/test/harness`, and test what the owner sees.

```tsx
import { renderApp, screen, stubFetch } from "@/test/harness";

it("warns about a quarter outside group 3", async () => {
  stubFetch({
    "GET /api/periods/{year}": { year: 2026, balances: {}, group3Quarters: [3, 4] },
  });
  renderApp(<PeriodSelect id="period" year={2026} kind="SingleTax" value="q1" onChange={() => {}} />);

  expect(await screen.findByText("Цей період поза групою 3: …")).toBeVisible();
});
```

- `renderApp(ui, { locale })` wraps the component in the i18n provider with the real `messages/uk.json` (the
  default) or `messages/ru.json`, and in a fresh `QueryClient` with retries off. It returns Testing
  Library's result plus `user` (user-event), so use `await user.click(...)`. A hook that sets its own
  `retry` still retries once, so keep a failing request out of the test or answer it with a status.
- Find elements by role and label in the owner's language: `getByRole("button", { name: "Сплатити" })`,
  `getByLabelText("Сума, ₴")`. Write every text as the owner reads it and give each component at least one
  assertion in `ru`. A renamed or missing catalog key then fails a test.
- Assert visible text and the requests sent. Do not assert state, props of children or markup, and do not
  take snapshots.
- Keep `Intl` output (dates, month names, grouped numbers) out of exact assertions, or normalise it as
  `shared/lib/dates.test.ts` does. CI's Node ICU differs from a laptop's. The suite runs in
  `America/Los_Angeles` on purpose.

## Stubbing the API: `stubFetch`

```ts
const api = stubFetch({
  "GET /api/payment-details": (request) => ({ purpose: "…", amountKop: request.query.amountKop }),
  "POST /api/declarations/{year}/{quarter}/files": reply(409, { title: "Not ready" }),
});
expect(api.requestsTo("GET /api/payment-details")).toHaveLength(2);
expect(api.requests[0]).toEqual({ method: "GET", path: "…", query: { … }, body: undefined });
```

- A key is `"METHOD /path"` exactly as the generated types in `data/api/schema.d.ts` spell it, with the
  `{param}` placeholders left in. A method or path the schema does not have fails the typecheck.
- The value is the success body, typed as a deep partial of the schema's response: set only what the
  component reads. In an inline literal, a misspelled field or a wrong type fails the typecheck; a field
  the schema later makes required does not.
- That check has gaps. A fixture built elsewhere and passed in is checked only for the fields it shares
  with the type, so an extra or renamed field slips through. Annotate shared fixtures and handler returns
  with the response type (`: PaymentDetails`) to get the full check. `reply()` bodies are untyped.
- A function gets the recorded request (`method`, concrete `path`, `query`, parsed JSON `body`) and may
  return a promise, to hold an answer back. `reply(status, body?, headers?)` answers a failure.
- Every test starts with no routes. A request no route covers, or one sent to another origin than the page's, gets a 501 and fails the test after it ends,
  so a component cannot call an endpoint the test did not plan for.

`vitest.setup.ts` installs the stub as the global `fetch` before the api client is imported, because the
client takes `fetch` once, when it is created. It also lets `new Request("/api/...")` take a relative url,
as a browser does.

## Fake timers: `useFakeTimers`

Use it for debounces and delays. It fakes `setTimeout`, `clearTimeout` and `Date`, and teaches Testing
Library to cope, which it cannot do under Vitest on its own.

```ts
const timers = useFakeTimers();
const { user } = renderApp(<Thing />, {}, timers.userOptions);
await user.click(...);
await timers.advance(500);
```

The clock moves only through `timers.advance(ms)`. A request and its response settle on promises, not on
the clock, so advance a millisecond at a time until the thing you wait for appears.

## Navigation: `router`, `setLocation`

`next/navigation` is mocked for every test. `setLocation("/settings?tab=treasury")` sets what
`usePathname` and `useSearchParams` return; `router.push` and the rest are spies to assert where the owner
was sent. Both reset after each test.
