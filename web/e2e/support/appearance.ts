import { expect, type APIRequestContext, type BrowserContext } from "@playwright/test";
import { localeChosenAtCookieName, localeCookieName } from "../../src/i18n/locales";

// A browser that chose this language just now. The newest choice wins (ADR-032), so a language cookie
// without its time would give way to whatever the server holds; signed in, the page then saves this one.
export async function chooseLanguage(context: BrowserContext, baseURL: string, locale: "uk" | "ru") {
  await context.addCookies([
    { name: localeCookieName, value: locale, url: baseURL },
    { name: localeChosenAtCookieName, value: encodeURIComponent(new Date().toISOString()), url: baseURL },
  ]);
}

// Puts the server back on Ukrainian and the system theme as the newest choice, so a later spec, whose
// browser has no choice of its own, opens in Ukrainian.
export async function resetAppearance(owner: APIRequestContext) {
  const response = await owner.put("/api/settings/appearance", {
    data: { locale: "uk", theme: "system", chosenAt: new Date().toISOString() },
  });
  expect(response.ok(), `resetting the appearance answered ${response.status()}`).toBe(true);
}
