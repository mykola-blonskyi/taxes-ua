"use client";

import { useSerwist } from "@serwist/next/react";
import { useEffect, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { Button } from "@/shared/ui/button";

const recheckEveryMs = 60 * 60 * 1000;

// A new service worker waits until the owner accepts this prompt, so a half-filled form is never
// reloaded under them and the open page never mixes chunks from two builds. The worker is also
// re-checked when the tab comes back to the front and hourly, because a tab left open for days
// would otherwise only learn about a deploy on its next full navigation.
export function UpdatePrompt() {
  const { serwist } = useSerwist();
  const t = useTranslations("pwa");
  const [available, setAvailable] = useState(false);
  const accepted = useRef(false);
  // True when another tab already activated the new worker: nothing is waiting, so only a reload helps.
  const alreadyActive = useRef(false);

  useEffect(() => {
    if (!serwist) {
      return;
    }

    const onWaiting = () => {
      alreadyActive.current = false;
      setAvailable(true);
    };
    // Fires in this tab once the new worker takes over. Reload only when this tab asked for it; another
    // tab's update leaves this one running old chunks, so it gets the prompt instead.
    // The very first install also takes control of the page (clientsClaim), which is not an update.
    const onControlling = (event: { isUpdate?: boolean }) => {
      if (accepted.current) {
        window.location.reload();
      } else if (event.isUpdate) {
        alreadyActive.current = true;
        setAvailable(true);
      }
    };
    const check = () => void serwist.update().catch(() => undefined);
    const onVisible = () => {
      if (document.visibilityState === "visible") {
        check();
      }
    };

    serwist.addEventListener("waiting", onWaiting);
    serwist.addEventListener("controlling", onControlling);
    document.addEventListener("visibilitychange", onVisible);
    const timer = window.setInterval(check, recheckEveryMs);

    return () => {
      serwist.removeEventListener("waiting", onWaiting);
      serwist.removeEventListener("controlling", onControlling);
      document.removeEventListener("visibilitychange", onVisible);
      window.clearInterval(timer);
    };
  }, [serwist]);

  if (!available) {
    return null;
  }

  return (
    <div
      role="status"
      className="fixed inset-x-4 bottom-20 z-50 flex items-center gap-3 rounded-lg border bg-background p-3 shadow-lg md:inset-x-auto md:right-4 md:bottom-4 md:max-w-sm"
    >
      <p className="min-w-0 flex-1 text-sm">{t("updateAvailable")}</p>
      <Button
        size="sm"
        onClick={() => {
          if (alreadyActive.current) {
            window.location.reload();
            return;
          }
          accepted.current = true;
          serwist?.messageSkipWaiting();
        }}
      >
        {t("reload")}
      </Button>
    </div>
  );
}
