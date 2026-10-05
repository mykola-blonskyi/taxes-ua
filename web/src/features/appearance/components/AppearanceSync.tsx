"use client";

import { useRouter } from "next/navigation";
import { useLocale } from "next-intl";
import { useTheme } from "next-themes";
import { useEffect, useRef } from "react";
import { useAppearance, type Appearance } from "@/data/settings/appearance";
import { useSettings } from "@/data/settings/useSettings";
import { localeCookieName } from "@/i18n/locales";
import { setLocale } from "@/i18n/setLocale";

// next-themes' own storage key.
const themeStorageKey = "theme";

function hasLocaleChoice(): boolean {
  return document.cookie.split("; ").some((entry) => entry.startsWith(`${localeCookieName}=`));
}

function hasThemeChoice(): boolean {
  try {
    return window.localStorage.getItem(themeStorageKey) !== null;
  } catch {
    return false;
  }
}

// Mounted once the owner is signed in. When the settings arrive it decides, once per load:
//  - a choice the server has not acknowledged (the menu was used a moment ago and the request was cut off
//    or failed) is sent again;
//  - a browser with no choice of its own, a new device, takes the server's language and theme, so it
//    matches the owner's other devices from the first screen after sign-in;
//  - a browser that already has a choice keeps it: a choice made in the menu is explicit and wins, and is
//    already saved to the server by the menu.
// "system" is a choice like the others: it syncs as that literal value and each device resolves it for
// itself. What remains is one paint in the browser's own default (Ukrainian, system) on a new device
// before the server's value arrives, and no pull of a change the owner made on another device into a
// browser that has a choice of its own.
export function AppearanceSync() {
  const { data } = useSettings();
  const { save, pending } = useAppearance();
  const router = useRouter();
  const locale = useLocale();
  const { theme, setTheme } = useTheme();
  const done = useRef(false);

  useEffect(() => {
    if (!data || done.current) {
      return;
    }
    done.current = true;

    const waiting: Appearance = pending();

    if (waiting.locale === undefined && !hasLocaleChoice() && data.locale !== locale) {
      setLocale(data.locale);
      router.refresh();
    }
    if (waiting.theme === undefined && !hasThemeChoice() && data.theme !== (theme ?? "system")) {
      setTheme(data.theme);
    }
    if (waiting.locale !== undefined || waiting.theme !== undefined) {
      save(waiting);
    }
  }, [data, pending, save, router, locale, theme, setTheme]);

  return null;
}
