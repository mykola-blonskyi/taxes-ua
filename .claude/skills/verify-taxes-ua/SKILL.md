---
name: verify-taxes-ua
description: Drive the taxes-ua web interface in a real browser to prove a screen works. Use when shipping or reviewing any UI ticket, when checking a screen at 375px for horizontal scroll, when checking theme or locale, when a screen must be reached behind the auth gate, or when you need a real session in a local stack without a Google OAuth client.
---

# Verify taxes-ua

Every screen sits behind an auth gate, so the honest way to see one is to hold a real session and
drive a real browser. This skill gets you both, and ships the script that measures every screen so
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
run. Chrome accepts the cookie over `http://localhost` despite its `Secure` flag, because localhost
counts as a trustworthy origin.

Sign in through this seam rather than stubbing auth in the browser. Read trap 1 before you consider
intercepting a request.

## 4. Measure

```
node .claude/skills/verify-taxes-ua/scripts/measure-screens.mjs --width 375
node .claude/skills/verify-taxes-ua/scripts/measure-screens.mjs --width 1280 --height 900
node .claude/skills/verify-taxes-ua/scripts/measure-screens.mjs --width 375 --locale ru --theme dark
```

The script launches headless Chrome over the DevTools Protocol with no package dependency, signs in
through the seam, and for every route reports `scrollWidth`, `clientWidth`, disclaimer presence,
nav width, `html lang` and the resolved theme, with a screenshot each. It exits non-zero when a
route overflows, loses its disclaimer, or renders the wrong locale, and it names the offending
element when a route is wider than its viewport.

Options are `--base --email --width --height --locale --theme --out --port --timeout`; pass
`CHROME_PATH` if it cannot find a browser.

It discovers routes by walking `web/src/app/**/page.tsx`, dropping route groups, and it fails if
`web/src/shared/constants/navigation.ts` links a route with no page. So a screen added by a later
ticket is measured with no edit here, and this file states no route list that could go stale. A
route with a dynamic segment is reported as skipped, because it needs seeded data; drive those by
hand and say so in your report.

**Acceptance for any UI ticket.** `scrollWidth` equals `clientWidth` on every route at 375px.
Greater means horizontal scroll, which is a defect to fix, not to report.

Check theme and locale by rendered state, never by what a toggle's label says. The script does this
for you. It reads theme from the `dark` or `light` class the provider resolves onto `<html>` and from
the computed `body` background, and locale from `html lang` and from the disclaimer text present in
`web/messages/<locale>.json`. A toggle can read "Темна" while the page renders light.

## 5. Evidence

Screenshots and one JSON report per run land in `.verify/` at the repository root, which is
git-ignored. Name every file you rely on in your report, and quote the measured numbers rather than
the verdict. Cleanup never touches `.verify/`.

## 6. Cleanup

```
docker compose -f docker-compose.yml -f docker-compose.local.yml down
```

Tear down only the stack you started. The script kills its own Chrome and uses a throwaway profile,
so nothing survives a crashed run except the evidence.

## Traps that have already cost a verifier its verdict

**1. An interception outlives the call that set it.** A `page.route` handler, a patched `fetch` or
an `initScript` stub installed to fake `/api/auth/me` stays installed, and answers 200 to a later
call that was meant to test the real thing. Worse, a faked session proves nothing about a screen
that a real session renders differently. Use the seam in step 3, which is a real sign-in, and leave
the network alone.

**2. A no-JS context measures an empty shell and calls it clean.** This is an App Router app. The
markup a JS-less client receives is a `<div hidden>` placeholder, and the real content arrives in an
RSC stream that needs client JS to paint. Such a run reports a perfect `scrollWidth: 375` next to
`hasDisclaimer: false, hasNav: false`, and those last two are the tell that the number is measuring
nothing. Treat any run reporting no disclaimer as a failed measurement, whatever the width says.
Always drive with JavaScript enabled.

**3. Wait for an element, never for a paint or a timeout.** `AuthGate` renders `null` from first
paint until `useMe()` resolves, so the load event, a screenshot on a timer, and CPU throttling with
Slow 3G all capture a blank page that looks like a broken screen. Gate every measurement on a real
element being present with real text; the script waits for `main h2`.

## Driving by hand

The script covers width, theme, locale and the disclaimer. For anything else (clicking the theme
menu, switching language, signing out, a form a later ticket adds) drive the browser with the
Playwright or chrome-devtools MCP tools. Sign in through the seam first, then prefer the stable
handles this app already exposes, which are the `aria-label` on each toggle (`nav.label`,
`theme.label`, `language.label`, `account.signOut` in `web/messages/uk.json`) and `aria-current` on
the active nav link. Exercise the real user path, and capture both the action and the resulting
state.
