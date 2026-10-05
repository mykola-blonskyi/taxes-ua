"use client";

import { useRouter } from "next/navigation";
import { useLocale } from "next-intl";
import { useTheme } from "next-themes";
import { useEffect } from "react";
import {
  appearanceFields,
  browserChoice,
  rememberChosenAt,
  resolve,
  serverChoice,
  useAppearance,
} from "@/data/settings/appearance";
import { useSettings } from "@/data/settings/useSettings";
import { setLocale } from "@/i18n/setLocale";

// Mounted once the owner is signed in. Each time the settings arrive (the load, and every return to the
// tab) it compares the browser's choice with the server's, field by field, and the later one wins: a newer
// server value is applied here, a newer browser value (a choice made signed out, or a save that never got
// through) is sent. The first paint keeps the browser's own value; the server's replaces it when it comes.
// Once both sides agree on a field's time it resolves to keep, so running again changes nothing.
export function AppearanceSync() {
  const { data } = useSettings({ refetchOnWindowFocus: "always" });
  const { send } = useAppearance();
  const router = useRouter();
  const locale = useLocale();
  const { setTheme } = useTheme();

  useEffect(() => {
    if (!data) {
      return;
    }

    for (const field of appearanceFields) {
      const browser = browserChoice(field);
      const server = serverChoice(data, field);
      const resolution = resolve(browser, server);

      if (resolution === "send" && browser !== null) {
        send(field, browser);
      } else if (resolution === "take" && field === "locale") {
        setLocale(server.value);
        rememberChosenAt(field, server.chosenAt);
        // Only when the language changes, and only once the cookie holds it: a browser that refuses the
        // cookie would otherwise refresh on every load.
        if (server.value !== locale && browserChoice(field)?.value === server.value) {
          router.refresh();
        }
      } else if (resolution === "take") {
        setTheme(server.value);
        rememberChosenAt(field, server.chosenAt);
      }
    }
  }, [data, send, locale, router, setTheme]);

  return null;
}
