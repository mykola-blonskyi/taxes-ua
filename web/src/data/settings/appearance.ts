"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useCallback } from "react";
import { api } from "@/data/api/client";
import { meQueryKey } from "@/data/auth/useMe";
import { settingsQueryKey } from "./useSettings";

// The shell's language menu and theme toggle own the owner's language and theme. Each writes the browser
// first (the cookie and next-themes' storage, so the change is instant and works signed out) and, when
// signed in, tells the server too, so another device and the messages the server writes follow.
export type Appearance = { locale?: string; theme?: string };

// A choice the server has not acknowledged yet. It survives a navigation or a dropped connection, and the
// next signed-in load sends it again before it lets the server's value overwrite the browser's.
const pendingKey = "appearance-pending";

function readPending(): Appearance {
  try {
    const parsed: unknown = JSON.parse(window.localStorage.getItem(pendingKey) ?? "{}");

    return typeof parsed === "object" && parsed !== null ? (parsed as Appearance) : {};
  } catch {
    return {};
  }
}

function writePending(pending: Appearance) {
  try {
    if (Object.keys(pending).length === 0) {
      window.localStorage.removeItem(pendingKey);
    } else {
      window.localStorage.setItem(pendingKey, JSON.stringify(pending));
    }
  } catch {
    // Storage can be blocked; the choice then only lasts until the server has it.
  }
}

export function useAppearance() {
  const queryClient = useQueryClient();

  // Fire and forget: the caller never waits, so a navigation started the same instant is not held up.
  // keepalive lets the request finish even when that navigation unloads the page.
  const save = useCallback(
    (change: Appearance) => {
      if (!queryClient.getQueryData(meQueryKey)) {
        return;
      }

      writePending({ ...readPending(), ...change });
      void api
        .PUT("/api/settings/appearance", { body: change, keepalive: true })
        .then(() => {
          const pending = readPending();
          for (const key of Object.keys(change) as (keyof Appearance)[]) {
            if (pending[key] === change[key]) {
              delete pending[key];
            }
          }
          writePending(pending);
          void queryClient.invalidateQueries({ queryKey: settingsQueryKey });
        })
        .catch(() => {
          // Left pending: the next load sends it again.
        });
    },
    [queryClient],
  );

  const pending = useCallback(() => readPending(), []);

  return { save, pending };
}
