/**
 * API client for the FleetStream BFF.
 *
 * - Handles JWT bearer token injection.
 * - Parses RFC 7807 error responses.
 * - Supports cursor-based pagination.
 *
 * This module uses `NEXT_PUBLIC_API_BASE_URL` at runtime.
 * It is safe to import from Client Components.
 */

import { env } from "./env";
import type { ProblemDetails } from "./types";
import { createLogger } from "./logger";

const log = createLogger("api-client");

// ─── Errors ───────────────────────────────────────────────────

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly problem: ProblemDetails,
  ) {
    super(`API ${status}: ${problem.title}`);
    this.name = "ApiError";
  }

  /** Correlation ID from the BFF response (for support debugging). */
  get correlationId(): string | undefined {
    return this.problem.correlationId;
  }

  /** Trace ID from the BFF response (W3C trace context). */
  get traceId(): string | undefined {
    return this.problem.traceId;
  }
}

// ─── Token management ─────────────────────────────────────────────

const TOKEN_KEY = "fs_access_token";
const TOKEN_EXPIRY_KEY = "fs_token_expires_at";

export function getStoredToken(): string | null {
  if (typeof window === "undefined") return null;
  const token = sessionStorage.getItem(TOKEN_KEY);
  const expiresAt = sessionStorage.getItem(TOKEN_EXPIRY_KEY);
  if (!token || !expiresAt) return null;
  if (new Date(expiresAt).getTime() <= Date.now()) {
    clearStoredToken();
    return null;
  }
  return token;
}

export function storeToken(token: string, expiresAt: string): void {
  sessionStorage.setItem(TOKEN_KEY, token);
  sessionStorage.setItem(TOKEN_EXPIRY_KEY, expiresAt);
}

export function clearStoredToken(): void {
  sessionStorage.removeItem(TOKEN_KEY);
  sessionStorage.removeItem(TOKEN_EXPIRY_KEY);
}

// ─── Retry config ─────────────────────────────────────────────

const MAX_RETRIES = 2;
const BASE_DELAY_MS = 500;

/** Exponential backoff with full jitter. */
function getRetryDelay(attempt: number): number {
  const exponential = BASE_DELAY_MS * Math.pow(2, attempt);
  return Math.random() * exponential;
}

/** Only retry on 5xx and network errors, never on 4xx. */
function isRetryable(status: number): boolean {
  return status >= 500;
}

// ─── Fetch wrapper ────────────────────────────────────────────

interface FetchOptions extends RequestInit {
  /** Override token (e.g. for initial dev-token call). */
  token?: string;
}

async function request<T>(
  path: string,
  options: FetchOptions = {},
): Promise<T> {
  const { token: overrideToken, ...init } = options;
  const token = overrideToken ?? getStoredToken();

  const headers = new Headers(init.headers);
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }
  if (
    init.body &&
    typeof init.body === "string" &&
    !headers.has("Content-Type")
  ) {
    headers.set("Content-Type", "application/json");
  }

  const url = `${env.apiBaseUrl}${path}`;
  let lastError: Error | undefined;

  for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
    if (attempt > 0) {
      const delay = getRetryDelay(attempt - 1);
      log.info("Retrying request", {
        path,
        attempt: attempt + 1,
        delayMs: Math.round(delay),
      });
      await new Promise((r) => setTimeout(r, delay));
    }

    let res: Response;
    try {
      res = await fetch(url, { ...init, headers });
    } catch (err) {
      lastError = err instanceof Error ? err : new Error(String(err));
      log.warn("Network error", {
        path,
        attempt: attempt + 1,
        error: lastError.message,
      });
      continue;
    }

    if (!res.ok) {
      let problem: ProblemDetails;
      try {
        problem = await res.json();
      } catch {
        problem = {
          title: res.statusText || "Unknown error",
          status: res.status,
        };
      }

      const apiError = new ApiError(res.status, problem);

      if (isRetryable(res.status) && attempt < MAX_RETRIES) {
        log.warn("Retryable server error", {
          path,
          status: res.status,
          attempt: attempt + 1,
          correlationId: problem.correlationId,
          traceId: problem.traceId,
        });
        lastError = apiError;
        continue;
      }

      log.error("API error", {
        path,
        status: res.status,
        correlationId: problem.correlationId,
        traceId: problem.traceId,
      });
      throw apiError;
    }

    // 204 No Content
    if (res.status === 204) return undefined as T;

    return res.json() as Promise<T>;
  }

  log.error("All retries exhausted", { path });
  throw lastError ?? new Error(`Request to ${path} failed after retries`);
}

// ─── Public API methods ───────────────────────────────────────────

/** Call the BFF dev-token endpoint to obtain a JWT. */
export async function requestDevToken(
  subject: string,
  roles?: string[],
) {
  return request<{ accessToken: string; expiresAt: string }>(
    "/api/v1/auth/dev-token",
    {
      method: "POST",
      body: JSON.stringify({ subject, roles }),
    },
  );
}

/** Convenience wrapper for GET requests with optional query params. */
export function apiGet<T>(
  path: string,
  params?: Record<string, string | number | boolean | undefined>,
) {
  let url = path;
  if (params) {
    const qs = new URLSearchParams();
    for (const [key, val] of Object.entries(params)) {
      if (val !== undefined) qs.set(key, String(val));
    }
    const str = qs.toString();
    if (str) url += `?${str}`;
  }
  return request<T>(url);
}

/** Convenience wrapper for POST requests with a JSON body. */
export function apiPost<T>(path: string, body?: unknown) {
  return request<T>(path, {
    method: "POST",
    body: body ? JSON.stringify(body) : undefined,
  });
}
