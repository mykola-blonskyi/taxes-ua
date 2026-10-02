// The page policy. Next's hydration payload is inline <script> tags, so a policy without a nonce needs
// script-src 'unsafe-inline', which gives no protection against an injected inline script. The proxy
// mints a nonce per request, Next stamps it on its own scripts, and 'strict-dynamic' lets those load the
// chunks they need. Styles keep 'unsafe-inline': React writes style attributes, which a nonce cannot cover.
export function contentSecurityPolicy(nonce: string): string {
  return [
    "default-src 'self'",
    `script-src 'self' 'nonce-${nonce}' 'strict-dynamic'`,
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: blob:",
    "font-src 'self'",
    "connect-src 'self'",
    "worker-src 'self'",
    "manifest-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
    "upgrade-insecure-requests",
  ].join("; ");
}

// For the paths the proxy does not see: a last segment with an extension is a file or, for an unknown
// address such as /foo.bar, the 404 page. Static pages that run script (the offline page) keep it in
// their own files, so 'self' is all they need.
export const staticContentSecurityPolicy = [
  "default-src 'self'",
  "script-src 'self'",
  "style-src 'self'",
  "img-src 'self' data:",
  "object-src 'none'",
  "base-uri 'self'",
  "form-action 'self'",
  "frame-ancestors 'none'",
].join("; ");

export function newNonce(): string {
  return btoa(crypto.randomUUID());
}
