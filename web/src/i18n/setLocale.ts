"use server";

import { cookies } from "next/headers";
import { localeCookieName, parseLocale } from "./locales";

export async function setLocale(value: string) {
  const store = await cookies();

  store.set(localeCookieName, parseLocale(value), {
    path: "/",
    maxAge: 60 * 60 * 24 * 365,
    sameSite: "lax",
  });
}
