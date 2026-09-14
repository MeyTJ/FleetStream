"use client";

/**
 * Minimal JWT payload helpers (client-side, dev-token only).
 *
 * The BFF dev-token issuer emits HS256 JWTs whose payloads carry:
 *   - `sub`  → subject (operator id)
 *   - `role` → roles (JwtSecurityTokenHandler maps ClaimTypes.Role → `role`;
 *              multiple roles are serialized as an array)
 *
 * These helpers drive UI gating only (e.g. hiding the Ack button without
 * `alerts:ack`). The BFF re-validates every role server-side — they are
 * never a security boundary.
 */

// ClaimTypes.Role as serialized by the default outbound claim type map.
const LEGACY_ROLE_CLAIM =
  "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

function base64UrlDecode(segment: string): string {
  const padded = segment + "=".repeat((4 - (segment.length % 4)) % 4);
  const base64 = padded.replace(/-/g, "+").replace(/_/g, "/");
  return atob(base64);
}

/** Decode the payload segment of a JWT; null when malformed or unsupported. */
export function decodeJwtPayload(
  token: string,
): Record<string, unknown> | null {
  if (typeof atob === "undefined") return null;
  try {
    const segment = token.split(".")[1];
    if (!segment) return null;
    return JSON.parse(base64UrlDecode(segment)) as Record<string, unknown>;
  } catch {
    return null;
  }
}

/** Extract role claims ("role"/"roles" + legacy URI form) as a string array. */
export function getJwtRoles(token: string | null): string[] {
  if (!token) return [];
  const payload = decodeJwtPayload(token);
  if (!payload) return [];

  const candidates = [payload.roles, payload.role, payload[LEGACY_ROLE_CLAIM]];
  for (const claim of candidates) {
    if (typeof claim === "string") return [claim];
    if (
      Array.isArray(claim) &&
      claim.every((entry) => typeof entry === "string")
    ) {
      return claim as string[];
    }
  }
  return [];
}

/** Extract the `sub` claim, or null. */
export function getJwtSubject(token: string | null): string | null {
  if (!token) return null;
  const payload = decodeJwtPayload(token);
  const sub = payload?.sub;
  return typeof sub === "string" && sub.length > 0 ? sub : null;
}

/** True when the token carries at least one of the required roles. */
export function hasAnyRole(
  token: string | null,
  ...required: string[]
): boolean {
  if (!token || required.length === 0) return false;
  const roles = getJwtRoles(token);
  return required.some((role) => roles.includes(role));
}
