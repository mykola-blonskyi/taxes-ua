# 02: Google sign-in and the interface shell

GitHub: #3
Status: ready-for-agent
Blocked by: none

## Parent

#1

## What to build

The owner signs in with their Google account; any other email is rejected. After sign-in, an empty home screen is visible with navigation for phone and desktop, light and dark theme, a language switch (Ukrainian by default, Russian second) and a disclaimer about the informational nature of the calculation. Backend: ASP.NET Core Identity schema v3, Google OAuth, a cookie behind Traefik, an allowlist from configuration, /auth/me and sign-out. Frontend: shadcn/ui, Tailwind, next-intl with uk and ru, TanStack Query, types generated from OpenAPI, a redirect to the sign-in page on 401.

## Acceptance criteria

- [ ] Signing in with an allowlisted email creates a session; `/api/auth/me` returns the user. A different email gets 403 with a clear message.
- [ ] The cookie is `HttpOnly; Secure; SameSite=Lax`; Google secrets live only in environment variables.
- [ ] The theme toggles manually and follows the system by default; the language toggles uk/ru, every visible string comes from the translation files.
- [ ] Every MVP placeholder screen opens on a phone at 375px with no horizontal scroll.
- [ ] API test: an unauthenticated request to a protected resource returns 401, an email outside the allowlist returns 403.

## Blocked by

- None (can start immediately)
