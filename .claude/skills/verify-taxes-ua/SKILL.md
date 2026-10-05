---
name: verify-taxes-ua
description: Drive the taxes-ua web interface in a real browser to prove a screen works. Use when shipping or reviewing any UI ticket, when checking a screen at 375px for horizontal scroll, when checking theme or locale, when a screen must be reached behind the auth gate, or when you need a real session in a local stack without a Google OAuth client.
---

# Verify taxes-ua

Every screen sits behind an auth gate, so the honest way to see one is to hold a real session and
drive a real browser. This skill gets you both, and points at the spec that measures every screen so
a reviewer reruns one command instead of reading your transcript.

Run every command from the repository root, which is your worktree root when you are working in one.

## 1. Launch

```
ALLOWED_EMAILS=owner@example.com docker compose -f docker-compose.yml -f docker-compose.local.yml up -d --build
```

`ALLOWED_EMAILS` is the whole allowlist. `docker-compose.local.yml` sets
`ASPNETCORE_ENVIRONMENT=Development`, which is what serves the OpenAPI document and the sign-in
seam in step 2. The Compose project is named after your worktree directory, so two lanes can run
side by side, but only one can own port 3000. When `up` fails with `address already in use`, find
the owner with `lsof -nP -iTCP:3000 -sTCP:LISTEN` and stop that specific PID; never kill by name.

Ready when `docker compose ps` reports `api` healthy and this answers:

```
curl -fsS http://localhost:3000/api/health
```

The api publishes no port of its own. Everything reaches it through `web`, which rewrites `/api/*`
to the container (`web/next.config.ts`).

## 2. Doctor

One read-only check that answers whether this instance is worth driving:

```
curl -fsS http://localhost:3000/api/health
curl -sS -o /dev/null -w 'gate=%{http_code}\n' http://localhost:3000/transactions
```

Expect `{"status":"ok","database":true}` and `gate=307`. A `gate=200` means `web/src/proxy.ts` is
not in the running image, so you are driving a stale build; rebuild before you measure anything.

## 3. Get a session

`GET /api/auth/login/development?email=<allowlisted>&returnUrl=/` signs the email in and redirects.
It exists only when the api runs in Development, and it reaches the session through the same
callback, allowlist and `SignInManager` a real Google sign-in uses, so the session it mints is a
real one. An email outside `ALLOWED_EMAILS` gets 403 from it, exactly as from Google.

```
curl -sS -c /tmp/taxesua.jar -L 'http://localhost:3000/api/auth/login/development?email=owner@example.com&returnUrl=%2F'
curl -sS -b /tmp/taxesua.jar http://localhost:3000/api/auth/me
```

In a browser, navigate to that same URL once; the session cookie is then held for the rest of the
run. Chrome accepts the cookie, named `__Host-taxesua.auth`, over `http://localhost` despite its
`Secure` flag, because localhost counts as a trustworthy origin.

Sign in through this seam rather than stubbing auth in the browser. Read trap 1 before you consider
intercepting a request.

## 4. Measure

```
cd web && pnpm e2e e2e/layout.spec.ts
```

`web/e2e/layout.spec.ts` visits every route at 375 px in Ukrainian and in Russian, signed in through the
Development seam, against seeded data so tables and cards hold rows. For each route, and for each tab
of a route, it asserts that the page does not scroll sideways (`scrollWidth <= clientWidth`), that the
disclaimer from `web/messages/<locale>.json` is in the rendered text, and that `html lang` is the locale.
A failure names the route, the locale, the tab and the offending element. It also runs axe-core on each state (serious and critical violations fail) and measures every control against 44 px, because the context is a touch phone (`pointer: coarse`). CI runs it in the `e2e` job.

It discovers routes by walking `web/src/app/**/page.tsx` and fails if
`web/src/shared/constants/navigation.ts` links a route with no page, so a screen added by a later ticket
is measured with no edit. A route with a dynamic segment fails until `dynamicRouteAddresses` in the spec
gives it a concrete address. `/login` is measured signed out, which is the only way a visitor sees it.

A strip that scrolls on its own, such as the settings tabs, passes only if the component carries a
`data-scroll-strip="<name>"` attribute and `scrollingStrips` in the spec lists that name for the route (and
tab) with a reason. Shape never matches. A box with `overflow: hidden` does not excuse its content: a child
wider than the box fails, unless the box truncates text with an ellipsis. A new strip is a layout
decision. `pnpm e2e e2e/layout.spec.ts` starts and removes its own Compose stack, as described under the regression
suite below.

**Acceptance for any UI ticket.** `pnpm e2e e2e/layout.spec.ts` passes. Horizontal scroll at 375 px is a defect to
fix, not to report.

To look at a width other than 375, or at the dark theme, drive the stack from step 1 by hand with the
Playwright or chrome-devtools MCP tools; the suite does not cover them.

Check theme and locale by rendered state, never by what a toggle's label says. The spec reads locale
from `html lang` and from the disclaimer text. For theme, read the `dark` or `light` class the provider
resolves onto `<html>` and the computed `body` background. A toggle can read "Темна" while the page
renders light.

## 5. Evidence

Quote the measured numbers or the failure message rather than the verdict. Screenshots you take by hand
and the passkey report go in `.verify/` at the repository root, which is git-ignored; name every file
you rely on in your report. Cleanup never touches `.verify/`.

## 6. Cleanup

```
docker compose -f docker-compose.yml -f docker-compose.local.yml down
```

Tear down only the stack you started. The script kills its own Chrome and uses a throwaway profile,
so nothing survives a crashed run except the evidence.

## Regression suite: `pnpm e2e`

```
cd web && pnpm exec playwright install chromium   # once
cd web && pnpm e2e
```

Runs the Playwright suite in `web/e2e/` (Chromium only) and is what CI runs as the `e2e` job. Global
setup builds the Compose stack under its own project name on a random port in 40000-49999 (never 3000),
points Telegram and monobank at in-process stubs through `TELEGRAM_BASE_URL` and `MONOBANK_BASE_URL`,
signs the owner in through the Development seam (step 3) and saves the session. Teardown runs
`docker compose down --volumes`, so one command leaves nothing behind. If the run is killed hard,
remove the leftover stack with `docker compose -p taxesua-e2e-<pid> down -v`; `docker ps` shows the name.

Each test seeds through the real API and asserts on a change it made itself, so no test depends on
another. A failing run leaves `web/e2e/playwright-report/` and a trace per failed test in
`web/e2e/test-results/`; open one with `pnpm exec playwright show-trace <trace.zip>`. Add a scenario
there when a new owner flow ships. The layout check in step 4 is part of this suite.

## Traps that have already cost a verifier its verdict

**1. An interception outlives the call that set it.** A `page.route` handler, a patched `fetch` or
an `initScript` stub installed to fake `/api/auth/me` stays installed, and answers 200 to a later
call that was meant to test the real thing. Worse, a faked session proves nothing about a screen
that a real session renders differently. Use the seam in step 3, which is a real sign-in, and leave
the network alone.

**2. A no-JS context measures an empty shell and calls it clean.** This is an App Router app. The
markup a JS-less client receives is a `<div hidden>` placeholder, and the real content arrives in an
RSC stream that needs client JS to paint. Such a run reports a perfect `scrollWidth: 375` next to
no disclaimer and no nav, and those last two are the tell that the number is measuring nothing. Treat
any run reporting no disclaimer as a failed measurement, whatever the width says. Always drive with
JavaScript enabled; the spec's disclaimer assertion fails such a run.

**3. Wait for an element, never for a paint or a timeout.** `AuthGate` renders `null` from first
paint until `useMe()` resolves, so the load event, a screenshot on a timer, and CPU throttling with
Slow 3G all capture a blank page that looks like a broken screen. Gate every measurement on a real
element being present with real text; the spec waits for `main h2`, for the seeded rows, and then for
the network to go quiet.

## Driving by hand

The layout spec covers width, locale and the disclaimer. For anything else (clicking the theme
menu, switching language, signing out, a form a later ticket adds) drive the browser with the
Playwright or chrome-devtools MCP tools. Sign in through the seam first, then prefer the stable
handles this app already exposes, which are the `aria-label` on each toggle (`nav.label`,
`theme.label`, `language.label`, `account.signOut` in `web/messages/uk.json`) and `aria-current` on
the active nav link. Exercise the real user path, and capture both the action and the resulting
state.

## Proving the passkey ceremony

```
node .claude/skills/verify-taxes-ua/scripts/verify-passkey.mjs --base http://localhost:3000
```

`scripts/verify-passkey.mjs` attaches a Chrome DevTools virtual authenticator over CDP, then runs the
real ceremony against the real endpoints from inside the page: register a passkey, sign out, clear
cookies, and sign back in with the passkey alone. It also posts a tampered credential to each submit
route and expects the rejection. It reports the RP ID the server sent, so a `ServerDomain` that does
not match the origin shows up as a failure rather than as a silent browser refusal. Report lands in
`.verify/passkey.json`.

A virtual authenticator is not a device. It exercises the genuine WebAuthn ceremony, the attestation
and assertion paths and the allowlist gate, and it says nothing about iOS Safari, Android Chrome, or
a hardware key. Name it as a virtual authenticator in your report and hand the owner the device steps
separately.
