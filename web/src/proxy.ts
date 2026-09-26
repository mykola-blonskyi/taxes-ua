import { NextResponse } from "next/server";
import type { NextRequest } from "next/server";

const SESSION_COOKIE = "taxesua.auth";

// Presence check only, never decode: the api owns the cookie and is the real authority, returning
// 401 when it is missing, expired, or otherwise invalid. This is a fast UX redirect, not a
// security boundary.
export function proxy(request: NextRequest) {
  return request.cookies.has(SESSION_COOKIE)
    ? NextResponse.next()
    : NextResponse.redirect(new URL("/login", request.url));
}

export const config = {
  // Default-deny, so a screen added by a later ticket is gated without editing this file. `login`
  // is the redirect target and would loop. `api` is rewritten to the api container, which owns its
  // own 401s and serves the sign-in endpoints themselves.
  matcher: ["/((?!login(?:/|$)|api(?:/|$)|_next/static|_next/image|favicon\\.ico|.*\\.(?:svg|png|jpg|jpeg|gif|webp|ico)$).*)"],
};
