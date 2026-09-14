import { describe, expect, it } from "vitest";
import { buildConnectSrc, buildSecurityHeaders, originPairs } from "./csp";

function cspOf(env: Record<string, string | undefined>): string {
  const header = buildSecurityHeaders(env).find(
    (h) => h.key === "Content-Security-Policy",
  );
  if (!header) throw new Error("CSP header missing");
  return header.value;
}

function directive(csp: string, name: string): string {
  return (
    csp
      .split("; ")
      .find((d) => d.startsWith(`${name} `))
      ?.slice(name.length + 1) ?? ""
  );
}

describe("originPairs", () => {
  it("returns both http and ws forms for a localhost origin", () => {
    expect(originPairs("http://localhost:8080")).toEqual([
      "http://localhost:8080",
      "ws://localhost:8080",
    ]);
  });

  it("upgrades to https/wss and drops the default port", () => {
    expect(originPairs("https://api.fleetstream.example.com/hubs/v1/fleet")).toEqual(
      [
        "https://api.fleetstream.example.com",
        "wss://api.fleetstream.example.com",
      ],
    );
  });

  it("keeps an explicit non-default port", () => {
    expect(originPairs("http://10.0.0.5:7070")).toEqual([
      "http://10.0.0.5:7070",
      "ws://10.0.0.5:7070",
    ]);
  });

  it("is empty for invalid or missing input instead of throwing", () => {
    expect(originPairs("not a url")).toEqual([]);
    expect(originPairs("")).toEqual([]);
    expect(originPairs(undefined)).toEqual([]);
  });

  it("ignores non-http schemes", () => {
    expect(originPairs("ftp://example.com")).toEqual([]);
  });
});

describe("buildConnectSrc", () => {
  it("always allows 'self' and the MapLibre tiles", () => {
    const value = buildConnectSrc({});
    expect(value).toContain("'self'");
    expect(value).toContain("https://*.maplibre.org");
  });

  it("falls back to the dev localhost origin when nothing is configured", () => {
    const value = buildConnectSrc({});
    expect(value).toContain("http://localhost:8080");
    expect(value).toContain("ws://localhost:8080");
  });

  it("derives a staging origin from the public env vars", () => {
    const value = buildConnectSrc({
      NEXT_PUBLIC_API_BASE_URL: "https://api.staging.fleetstream.io",
      NEXT_PUBLIC_SIGNALR_HUB_URL:
        "https://api.staging.fleetstream.io/hubs/v1/fleet",
    });
    expect(value).toContain("https://api.staging.fleetstream.io");
    expect(value).toContain("wss://api.staging.fleetstream.io");
  });

  it("never leaks localhost once a real API origin is configured", () => {
    // Regression guard: the original config hardcoded localhost, which made the
    // bundle unable to reach any deployed BFF.
    const value = buildConnectSrc({
      NEXT_PUBLIC_API_BASE_URL: "https://api.fleetstream.example.com",
      NEXT_PUBLIC_SIGNALR_HUB_URL:
        "https://api.fleetstream.example.com/hubs/v1/fleet",
    });
    expect(value).not.toContain("localhost");
  });

  it("allows the configured error-report collector origin", () => {
    // A collector that is not in connect-src would have every report blocked by
    // the browser, so error reporting would look wired up but silently no-op.
    const value = buildConnectSrc({
      NEXT_PUBLIC_API_BASE_URL: "https://api.fleetstream.example.com",
      NEXT_PUBLIC_ERROR_REPORT_URL:
        "https://errors.fleetstream.example.com/v1/client",
    });
    expect(value).toContain("https://errors.fleetstream.example.com");
  });

  it("does not add a websocket form for an https report endpoint", () => {
    // Reports are plain HTTPS POSTs; allowing the ws twin would widen the policy
    // for no reason.
    const value = buildConnectSrc({
      NEXT_PUBLIC_ERROR_REPORT_URL:
        "https://errors.fleetstream.example.com/v1/client",
    });
    expect(value).toContain("https://errors.fleetstream.example.com");
    expect(value).not.toContain("wss://errors.fleetstream.example.com");
  });

  it("adds no collector origin when the variable is unset or blank", () => {
    const without = buildConnectSrc({
      NEXT_PUBLIC_API_BASE_URL: "https://api.fleetstream.example.com",
    });
    const withBlank = buildConnectSrc({
      NEXT_PUBLIC_API_BASE_URL: "https://api.fleetstream.example.com",
      NEXT_PUBLIC_ERROR_REPORT_URL: "",
    });
    expect(withBlank).toBe(without);
  });

  it("de-duplicates when REST and hub share one origin", () => {
    const value = buildConnectSrc({
      NEXT_PUBLIC_API_BASE_URL: "https://api.example.com",
      NEXT_PUBLIC_SIGNALR_HUB_URL: "https://api.example.com/hubs/v1/fleet",
    });
    expect(value.match(/https:\/\/api\.example\.com/g)).toHaveLength(1);
  });

  it("derives the hub origin when only the API URL is set", () => {
    // A deployment that configures only NEXT_PUBLIC_API_BASE_URL must not have
    // the dev localhost hub fallback leak into its CSP.
    const value = buildConnectSrc({
      NEXT_PUBLIC_API_BASE_URL: "https://api.prod.example.com",
    });
    expect(value).toContain("wss://api.prod.example.com");
    expect(value).not.toContain("localhost");
  });

  it("honours an explicit CSP_CONNECT_SRC override", () => {
    const value = buildConnectSrc({
      CSP_CONNECT_SRC: "https://bff.internal wss://bff.internal",
      NEXT_PUBLIC_API_BASE_URL: "https://api.example.com",
    });
    expect(value).toBe(
      "'self' https://bff.internal wss://bff.internal https://*.maplibre.org",
    );
  });
});

describe("buildSecurityHeaders", () => {
  it("emits the four baseline hardening headers plus the CSP", () => {
    const keys = buildSecurityHeaders({}).map((h) => h.key);
    expect(keys).toEqual([
      "X-Frame-Options",
      "X-Content-Type-Options",
      "Referrer-Policy",
      "Permissions-Policy",
      "Content-Security-Policy",
    ]);
  });

  it("keeps click-jacking and framing protections intact", () => {
    const headers = buildSecurityHeaders({});
    const byKey = Object.fromEntries(headers.map((h) => [h.key, h.value]));
    expect(byKey["X-Frame-Options"]).toBe("DENY");
    expect(byKey["X-Content-Type-Options"]).toBe("nosniff");
    expect(directive(byKey["Content-Security-Policy"], "frame-ancestors")).toBe(
      "'none'",
    );
  });

  it("embeds the env-derived connect-src in the CSP", () => {
    const csp = cspOf({
      NEXT_PUBLIC_API_BASE_URL: "https://api.prod.example.com",
    });
    expect(directive(csp, "connect-src")).toContain(
      "https://api.prod.example.com",
    );
  });

  it("names every directive so the policy is well-formed", () => {
    // Regression guard: the connect-src value was once interpolated without its
    // directive name, which made the whole CSP ambiguous to browsers.
    const csp = cspOf({});
    const known = [
      "default-src",
      "script-src",
      "style-src",
      "img-src",
      "font-src",
      "connect-src",
      "frame-ancestors",
    ];
    for (const name of known) {
      expect(directive(csp, name), `missing directive: ${name}`).not.toBe("");
    }
    // Nothing may appear before the first directive name.
    expect(csp.startsWith("default-src 'self'; ")).toBe(true);
  });
});
