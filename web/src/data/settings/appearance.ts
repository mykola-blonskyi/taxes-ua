"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useCallback } from "react";
import { api } from "@/data/api/client";
import { meQueryKey } from "@/data/auth/useMe";
import { localeChosenAtCookieName, localeCookieName } from "@/i18n/locales";
import { settingsQueryKey, type SettingsResponse } from "./useSettings";

// The owner's language and theme, each kept with the moment it was chosen, so the newest choice made on
// any device wins (ADR-032). The browser keeps the time next to the value the page reads: the locale's
// in a cookie beside the `locale` cookie, the theme's in localStorage beside next-themes' key.
export type AppearanceField = "locale" | "theme";

export const appearanceFields: readonly AppearanceField[] = ["locale", "theme"];

// chosenAt is an ISO instant, or null for a value kept from before the times were (it counts as oldest).
export type Choice = { value: string; chosenAt: string | null };

export type Resolution = "keep" | "take" | "send";

const themeStorageKey = "theme";
const themeChosenAtKey = "theme-chosen-at";
const cookieMaxAge = 60 * 60 * 24 * 365;

function readCookie(name: string): string | null {
  const entry = document.cookie.split("; ").find((candidate) => candidate.startsWith(`${name}=`));

  return entry === undefined ? null : decodeURIComponent(entry.slice(name.length + 1));
}

function readStorage(key: string): string | null {
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

// The browser's own choice, or null when it has none (a new device, or one that never chose).
export function browserChoice(field: AppearanceField): Choice | null {
  const value = field === "locale" ? readCookie(localeCookieName) : readStorage(themeStorageKey);
  if (value === null) {
    return null;
  }

  return { value, chosenAt: field === "locale" ? readCookie(localeChosenAtCookieName) : readStorage(themeChosenAtKey) };
}

// Records when the value the browser now holds was chosen. The value itself is written by setLocale or
// next-themes, which own those keys.
export function rememberChosenAt(field: AppearanceField, chosenAt: string | null) {
  if (field === "locale") {
    document.cookie =
      chosenAt === null
        ? `${localeChosenAtCookieName}=; path=/; max-age=0; samesite=lax`
        : `${localeChosenAtCookieName}=${encodeURIComponent(chosenAt)}; path=/; max-age=${cookieMaxAge}; samesite=lax`;

    return;
  }
  try {
    if (chosenAt === null) {
      window.localStorage.removeItem(themeChosenAtKey);
    } else {
      window.localStorage.setItem(themeChosenAtKey, chosenAt);
    }
  } catch {
    // Storage can be blocked; the theme then lasts only for this page anyway.
  }
}

export function serverChoice(settings: SettingsResponse, field: AppearanceField): Choice {
  return field === "locale"
    ? { value: settings.locale, chosenAt: settings.localeChosenAt ?? null }
    : { value: settings.theme, chosenAt: settings.themeChosenAt ?? null };
}

function instant(chosenAt: string | null): number {
  const time = chosenAt === null ? Number.NaN : Date.parse(chosenAt);

  return Number.isNaN(time) ? Number.NEGATIVE_INFINITY : time;
}

// The later choice stands. A browser with no choice takes the server's; a missing time is the oldest; two
// choices from the same moment (or two without a time) leave each side as it is.
export function resolve(browser: Choice | null, server: Choice): Resolution {
  if (browser === null) {
    return "take";
  }
  const [mine, theirs] = [instant(browser.chosenAt), instant(server.chosenAt)];
  if (theirs > mine) {
    return "take";
  }

  return mine > theirs ? "send" : "keep";
}

export function useAppearance() {
  const queryClient = useQueryClient();

  // Never awaited, so a navigation started the same instant is not held up; keepalive lets the request
  // finish when that navigation unloads the page. Signed out there is no one to save it for: the browser
  // keeps the choice with its time, and the first signed-in load sends it.
  const send = useCallback(
    (field: AppearanceField, choice: Choice) => {
      if (!queryClient.getQueryData(meQueryKey) || choice.chosenAt === null) {
        return;
      }

      void api
        .PUT("/api/settings/appearance", { body: { [field]: choice.value, chosenAt: choice.chosenAt }, keepalive: true })
        .then(({ data }) => {
          if (!data) {
            return;
          }
          // The server may hold the choice at another time (it caps a clock that runs ahead); taking its
          // time keeps the two equal, so the next load does not send it again.
          const stored = serverChoice(data, field);
          if (stored.value === choice.value) {
            rememberChosenAt(field, stored.chosenAt);
          }
          queryClient.setQueryData(settingsQueryKey, data);
        })
        .catch(() => {
          // The browser still holds the newer choice, so the next load or focus sends it again.
        });
    },
    [queryClient],
  );

  // A choice made in a menu: the caller has already written the value, this stamps it and saves it.
  const choose = useCallback(
    (field: AppearanceField, value: string) => {
      const chosenAt = new Date().toISOString();
      rememberChosenAt(field, chosenAt);
      send(field, { value, chosenAt });
    },
    [send],
  );

  return { choose, send };
}
