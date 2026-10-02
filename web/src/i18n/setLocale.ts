import { localeCookieName, parseLocale } from "./locales";

// Written in the browser, not by a server action: the cookie is in place before this returns, so a
// navigation started in the same instant already carries it. An action's cookie only lands once its
// POST has come back, and a navigation that starts first runs with the old language.
export function setLocale(value: string) {
  const maxAge = 60 * 60 * 24 * 365;

  document.cookie = `${localeCookieName}=${parseLocale(value)}; path=/; max-age=${maxAge}; samesite=lax`;
}
