import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";

// Resolved at build time: rewrites are baked into the standalone server.
const apiUrl = process.env.API_URL ?? "http://localhost:8080";

// Nothing here uses the camera, microphone, location or payment sheet. Passkeys (publickey-credentials-*)
// are left out, so they keep the default of the page's own origin.
const permissionsPolicy = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

const securityHeaders = [
  { key: "Strict-Transport-Security", value: "max-age=31536000; includeSubDomains" },
  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "X-Frame-Options", value: "DENY" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  // The page policy carries a per-request nonce, so src/proxy.ts sets it.
  { key: "Permissions-Policy", value: permissionsPolicy },
];

const nextConfig: NextConfig = {
  output: "standalone",
  poweredByHeader: false,
  async headers() {
    return [{ source: "/:path*", headers: securityHeaders }];
  },
  async rewrites() {
    return {
      beforeFiles: [{ source: "/api/:path*", destination: `${apiUrl}/api/:path*` }],
      afterFiles: [],
      fallback: [],
    };
  },
};

const withNextIntl = createNextIntlPlugin();

export default withNextIntl(nextConfig);
