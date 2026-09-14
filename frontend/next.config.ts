import type { NextConfig } from "next";
import { buildSecurityHeaders } from "./src/lib/csp";

/**
 * Security response headers, built from environment configuration by
 * `src/lib/csp.ts` (see that module for the `connect-src` rationale and
 * `src/lib/csp.test.ts` for the assertions).
 */
const securityHeaders = buildSecurityHeaders(process.env);

const nextConfig: NextConfig = {
  // Emit a self-contained server bundle so the Docker image can ship just
  // .next/standalone + static/public instead of node_modules (see Dockerfile).
  output: "standalone",
  async headers() {
    return [
      {
        source: "/(.*)",
        headers: securityHeaders,
      },
    ];
  },
};

export default nextConfig;
