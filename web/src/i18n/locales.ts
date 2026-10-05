export const locales = ["uk", "ru"] as const;

export type Locale = (typeof locales)[number];

export const defaultLocale: Locale = "uk";

export const localeCookieName = "locale";

// When the language in the cookie above was chosen, so the newest choice from any device wins (ADR-032).
export const localeChosenAtCookieName = "locale-chosen-at";

export function parseLocale(value: string | undefined): Locale {
  return locales.find((locale) => locale === value) ?? defaultLocale;
}
