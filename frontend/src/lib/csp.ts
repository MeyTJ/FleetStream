/**
 * Content-Security-Policy construction for the FleetStream frontend.
 *
 * Extracted from `next.config.ts` so it is unit-testable and so `connect-src`
 * is derived from the environment rather than hardcoded to localhost — the
 * previous literal made the production bundle unable to reach any deployed BFF.
 *
 * Configuration:
 *   NEXT_PUBLIC_API_BASE_URL    BFF REST origin (also used to derive CSP hosts)
 *   NEXT_PUBLIC_SIGNALR_HUB_URL hub URL (also used to derive CSP hosts)
 *   CSP_CONNECT_SRC             optional space-separated override of the
 *                               environment-derived hosts, for deployments
 *                               where the browser reaches extra origins.
 *
 * This module must stay free of the `"use client"` directive: it is imported by
 * next.config.ts, which runs in Node during build.
 */

/** Minimal env shape so the builder is testable without mutating process.env. */
export type CspEnv = Record<string, string | undefined>;

export interface SecurityHeader {
  key: string;
  value: string;
}

/**
 * Return `[http(s)://host[:port], ws(s)://host[:port]]` for a URL so both the
 * REST and WebSocket schemes of a BFF origin are allowed. Invalid or empty
 * input yields `[]` rather than throwing during a build.
 */
export function originPairs(url: string | undefined): string[] {
  if (!url) return [];
  try {
    const parsed = new URL(url);
    if (!parsed.protocol.startsWith("http")) return [];
    const host = parsed.port
      ? `${parsed.hostname}:${parsed.port}`
      : parsed.hostname;
    const wsScheme = parsed.protocol === "https:" ? "wss:" : "ws:";
    return [
      `${parsed.protocol}//${host}`,
      `${wsScheme}//${host}`,
    ];
  } catch {
    return [];
  }
}

/** Local development fallbacks, matching src/lib/env.ts. */
const DEV_API_URL = "http://localhost:8080";
const DEV_HUB_URL = "http://localhost:8080/hubs/v1/fleet";

/** The `connect-src` directive value for a given environment. */
export function buildConnectSrc(env: CspEnv): string {
  const override = (env.CSP_CONNECT_SRC ?? "").split(/\s+/).filter(Boolean);
  const apiUrl = env.NEXT_PUBLIC_API_BASE_URL;
  // Derive the hub origin from the API when only one of the two is configured,
  // so a half-configured deployment never leaks the dev localhost fallback into
  // a production CSP.
  const hubUrl = env.NEXT_PUBLIC_SIGNALR_HUB_URL ?? apiUrl ?? DEV_HUB_URL;
  const hosts =
    override.length > 0
      ? override
      : [...originPairs(apiUrl ?? DEV_API_URL), ...originPairs(hubUrl)];

  // A client error collector is a third origin the browser must be allowed to
  // reach. Omitting it here would make error-reporting silently fail in
  // production while working in dev, which is the worst possible failure mode
  // for an observability feature.
  const reportUrl = env.NEXT_PUBLIC_ERROR_REPORT_URL;
  const reportHosts = reportUrl ? originPairs(reportUrl).slice(0, 1) : [];

  return [
    "'self'",
    // De-duplicate: the API and hub URL usually share one origin.
    ...Array.from(new Set([...hosts, ...reportHosts])),
    // MapLibre tile endpoints.
    "https://*.maplibre.org",
  ].join(" ");
}

/** Full set of security response headers applied to every route. */
export function buildSecurityHeaders(env: CspEnv): SecurityHeader[] {
  return [
    { key: "X-Frame-Options", value: "DENY" },
    { key: "X-Content-Type-Options", value: "nosniff" },
    {
      key: "Referrer-Policy",
      value: "strict-origin-when-cross-origin",
    },
    {
      key: "Permissions-Policy",
      value: "camera=(), microphone=(), geolocation=()",
    },
    {
      key: "Content-Security-Policy",
      value: [
        "default-src 'self'",
        "script-src 'self' 'unsafe-inline' 'unsafe-eval'",
        "style-src 'self' 'unsafe-inline'",
        "img-src 'self' data: blob: https://demotiles.maplibre.org",
        "font-src 'self' https://fonts.gstatic.com",
        `connect-src ${buildConnectSrc(env)}`,
        "frame-ancestors 'none'",
      ].join("; "),
    },
  ];
}
