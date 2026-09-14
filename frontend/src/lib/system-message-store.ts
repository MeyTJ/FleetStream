"use client";

/**
 * Store for OnSystemMessage (SignalR protocol §3.3) — server-originated ops
 * notices such as planned maintenance, backpressure warnings and bulk presence
 * loss.
 *
 * Only the newest notice is surfaced: these are transient operational states
 * rather than an event log, and stacking banners degrades the dashboard under
 * exactly the load that produces them. Notices self-expire so a stale warning
 * does not linger after the condition clears.
 */

import { useSyncExternalStore } from "react";

export type SystemMessageSeverity = "info" | "warn" | "error";

export interface SystemMessage {
  severity: SystemMessageSeverity;
  /** Stable machine-readable key, e.g. "presence.bulk_offline". */
  code: string;
  /** Human-readable text intended for the banner. */
  message: string;
  /** Server-side timestamp (ISO 8601, UTC). */
  timestamp: string;
}

/** How long a notice stays visible before it is dropped. */
const TTL_MS = 5 * 60 * 1000;

let current: SystemMessage | null = null;
let listeners: Array<() => void> = [];
let expiry: ReturnType<typeof setTimeout> | null = null;

function emit(): void {
  for (const listener of listeners) listener();
}

function subscribe(listener: () => void): () => void {
  listeners = [...listeners, listener];
  return () => {
    listeners = listeners.filter((l) => l !== listener);
  };
}

function getSnapshot(): SystemMessage | null {
  return current;
}

/**
 * The wire shape of OnSystemMessage as delivered by SignalR. Typed loosely on
 * purpose: severity arrives as a plain string from the hub, so normalizing is
 * the store's job rather than the caller's.
 */
export interface SystemMessageInput {
  severity?: string;
  code?: string;
  message?: string;
  timestamp?: string;
}

/**
 * Record a notice, replacing any previous one. Returns false when the payload
 * was unusable, which keeps malformed frames from blanking a live banner.
 */
export function applySystemMessage(input: SystemMessageInput | null | undefined): boolean {
  const message = input?.message?.trim();
  if (!message) return false;

  current = {
    severity: normalizeSeverity(input?.severity),
    code: input?.code?.trim() || "unknown",
    message,
    timestamp: input?.timestamp ?? new Date().toISOString(),
  };

  if (expiry) clearTimeout(expiry);
  expiry = setTimeout(() => {
    current = null;
    expiry = null;
    emit();
  }, TTL_MS);

  emit();
  return true;
}

/** Dismiss the current notice. */
export function clearSystemMessage(): void {
  if (expiry) {
    clearTimeout(expiry);
    expiry = null;
  }
  if (current === null) return;
  current = null;
  emit();
}

function normalizeSeverity(severity?: string): SystemMessageSeverity {
  switch (severity?.toLowerCase()) {
    case "warn":
    case "warning":
      return "warn";
    case "error":
    case "err":
      return "error";
    default:
      return "info";
  }
}

/** Subscribe to the active system notice (null when none). */
export function useSystemMessage(): SystemMessage | null {
  return useSyncExternalStore(subscribe, getSnapshot, () => null);
}
