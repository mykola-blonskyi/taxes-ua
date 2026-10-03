import { NextResponse } from "next/server";
import type { NextRequest } from "next/server";
import { contentSecurityPolicy, newNonce } from "@/shared/security/csp";

const SESSION_COOKIE = "taxesua.auth";

// Presence check only, never decode: the api owns the cookie and is the real authority, returning
// 401 when it is missing, expired, or otherwise invalid. This is a fast UX redirect, not a
// security boundary.
export function proxy(request: NextRequest) {
  const { pathname } = request.nextUrl;
  const isLogin = pathname === "/login" || pathname.startsWith("/login/");
  if (!isLogin && !request.cookies.has(SESSION_COOKIE)) {
    return NextResponse.redirect(new URL("/login", request.url));
  }

  // `next dev` evaluates code for fast refresh, so the policy applies to production builds only.
  if (process.env.NODE_ENV !== "production") {
    return NextResponse.next();
  }

  // Next reads the nonce from the request's own Content-Security-Policy header and applies it to the
  // scripts it renders; x-nonce hands it to the layout for the theme script.
  const nonce = newNonce();
  const policy = contentSecurityPolicy(nonce);
  const headers = new Headers(request.headers);
  headers.set("x-nonce", nonce);
  headers.set("Content-Security-Policy", policy);
  const response = NextResponse.next({ request: { headers } });
  response.headers.set("Content-Security-Policy", policy);

  return response;
}

export const config = {
  // Default-deny for routes, so a screen added by a later ticket is gated without editing this file,
  // and every page gets a nonce. `api` is rewritten to the api container, which owns its own 401s and
  // serves the sign-in endpoints themselves. Anything whose last segment carries an extension is a
  // file, not a route: an enumerated extension list silently gated `manifest.json` and `sw.js`, which
  // made the app uninstallable because Chrome reads both unauthenticated.
  matcher: ["/((?!api(?:/|$)|_next/|.*\\.[^/]+$).*)"],
};
