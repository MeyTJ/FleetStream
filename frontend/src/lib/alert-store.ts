"use client";

/**
 * Alert store using useSyncExternalStore.
 *
 * - Ring buffer: max 500 alerts, newest first.
 * - Inbound: OnAlert from SignalR.
 * - Filter: client-side severity/truckId on the ring buffer.
 * - Ack: optimistic update; rollback on API failure.
 * - Purge: OnAlertsPurged trims entries older than the server cutoff.
 */

import { useSyncExternalStore } from "react";
import type { Alert } from "@/lib/types";

// ─── Constants ────────────────────────────────────────────────

const MAX_ALERTS = 500;

// ─── State ────────────────────────────────────────────────────

let alerts: Alert[] = [];
let listeners: Array<() => void> = [];

function emit() {
  for (const listener of listeners) {
    listener();
  }
}

function subscribe(listener: () => void): () => void {
  listeners = [...listeners, listener];
  return () => {
    listeners = listeners.filter((l) => l !== listener);
  };
}

function getSnapshot(): Alert[] {
  return alerts;
}

// ─── Actions ──────────────────────────────────────────────────

/** Push a new alert into the ring buffer (newest first). Deduplicates by id. */
export function pushAlert(alert: Alert): void {
  // Deduplicate: if we already have this alert id, update it (e.g. ack state)
  const existingIdx = alerts.findIndex((a) => a.id === alert.id);
  if (existingIdx >= 0) {
    const next = [...alerts];
    next[existingIdx] = alert;
    alerts = next;
  } else {
    alerts = [alert, ...alerts].slice(0, MAX_ALERTS);
  }
  emit();
}

/** Optimistically mark an alert as acknowledged. Returns the previous alert for rollback. */
export function optimisticAck(alertId: string, acknowledgedBy: string): Alert | undefined {
  const idx = alerts.findIndex((a) => a.id === alertId);
  if (idx < 0) return undefined;

  const prev = alerts[idx];
  const updated: Alert = {
    ...prev,
    isAcknowledged: true,
    acknowledgedBy,
    acknowledgedAt: new Date().toISOString(),
  };

  const next = [...alerts];
  next[idx] = updated;
  alerts = next;
  emit();
  return prev;
}

/** Rollback an optimistic ack (on API failure). */
export function rollbackAck(alertId: string, previous: Alert): void {
  const idx = alerts.findIndex((a) => a.id === alertId);
  if (idx < 0) return;

  const next = [...alerts];
  next[idx] = previous;
  alerts = next;
  emit();
}

/** Bulk replace alerts (e.g. from initial REST load). */
export function replaceAlerts(incoming: Alert[]): void {
  // Merge: incoming takes priority, keep existing that aren't in incoming
  const incomingIds = new Set(incoming.map((a) => a.id));
  const merged = [
    ...incoming,
    ...alerts.filter((a) => !incomingIds.has(a.id)),
  ];
  // Sort by timestamp desc, cap at MAX_ALERTS
  merged.sort(
    (a, b) =>
      new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime(),
  );
  alerts = merged.slice(0, MAX_ALERTS);
  emit();
}

/** Clear all alerts (e.g. on logout). */
export function clearAlerts(): void {
  alerts = [];
  purgeNotice = null;
  emit();
}

/** Mark an alert as acknowledged (confirmed by server). */
export function confirmAck(alertId: string, acknowledgedBy: string): void {
  const idx = alerts.findIndex((a) => a.id === alertId);
  if (idx < 0) return;

  const next = [...alerts];
  next[idx] = {
    ...next[idx],
    isAcknowledged: true,
    acknowledgedBy,
    acknowledgedAt: next[idx].acknowledgedAt ?? new Date().toISOString(),
  };
  alerts = next;
  emit();
}
// ─── Purge (OnAlertsPurged, SignalR §3.3) ────────────────────────

export interface AlertPurgeNotice {
  /** Alerts the server reported as purged (falls back to locally removed). */
  count: number;
  /** Exclusive cutoff — alerts strictly older than this were evicted. */
  beforeTimestamp: string;
  /** When the purge notice was received (ISO 8601). */
  receivedAt: string;
}

let purgeNotice: AlertPurgeNotice | null = null;

function getPurgeNotice(): AlertPurgeNotice | null {
  return purgeNotice;
}

/**
 * Trim alerts older than `beforeTimestamp` (server-side ring-buffer
 * eviction) and record a purge notice for the info banner.
 * Returns the number of locally removed entries.
 */
export function purgeAlerts(
  beforeTimestamp: string,
  reportedCount?: number,
): number {
  const cutoff = new Date(beforeTimestamp).getTime();
  if (!Number.isFinite(cutoff)) return 0;

  const kept = alerts.filter(
    (a) => new Date(a.timestamp).getTime() >= cutoff,
  );
  const removed = alerts.length - kept.length;
  alerts = kept;

  purgeNotice = {
    count: reportedCount ?? removed,
    beforeTimestamp,
    receivedAt: new Date().toISOString(),
  };
  emit();
  return removed;
}

/** Clear the current purge notice (dismiss the banner). */
export function clearPurgeNotice(): void {
  purgeNotice = null;
  emit();
}



// ─── Derived selectors ───────────────────────────────────────

/** Get unacknowledged alert count (for badge). */
export function getUnackedCount(): number {
  return alerts.filter((a) => !a.isAcknowledged).length;
}

// ─── React hooks ──────────────────────────────────────────────

/** Subscribe to the full alert list. */
export function useAlerts(): Alert[] {
  return useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
}

/** Subscribe to unacknowledged alert count. */
export function useUnackedAlertCount(): number {
  return useSyncExternalStore(
    subscribe,
    () => getUnackedCount(),
    () => 0,
  );
}

/** Subscribe to the latest server purge notice (for the info banner). */
export function usePurgeNotice(): AlertPurgeNotice | null {
  return useSyncExternalStore(subscribe, getPurgeNotice, () => null);
}
