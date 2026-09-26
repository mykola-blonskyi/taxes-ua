import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";

// Resolved at build time: rewrites are baked into the standalone server.
const apiUrl = process.env.API_URL ?? "http://localhost:8080";

const nextConfig: NextConfig = {
  output: "standalone",
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
