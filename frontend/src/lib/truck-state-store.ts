"use client";

/**
 * Zustand-like store for truck states on the map.
 *
 * Uses React useSyncExternalStore for optimal concurrent rendering.
 * Enforces ≤1 update per truck per 2 s (matching BFF rate limit).
 * On `OnFleetUpdate`, does a bulk replace.
 */

import { useSyncExternalStore } from "react";
import type { TruckState } from "@/lib/types";

// ─── State ────────────────────────────────────────────────────────

type TruckStateMap = Map<string, TruckState>;

let states: TruckStateMap = new Map();
let listeners: Array<() => void> = [];

// Per-truck throttle: last applied timestamp per truckId
const lastAppliedAt = new Map<string, number>();
const THROTTLE_MS = 2_000;

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

function getSnapshot(): TruckStateMap {
  return states;
}

// ─── Actions ──────────────────────────────────────────────────────

/** Apply a single truck state update (throttled to 1/truck/2s). */
export function applyTruckState(update: TruckState): void {
  const now = Date.now();
  const last = lastAppliedAt.get(update.truckId) ?? 0;

  // Always allow if the update is newer than what we have
  const existing = states.get(update.truckId);
  const isNewer =
    !existing ||
    new Date(update.timestamp).getTime() >
      new Date(existing.timestamp).getTime();

  if (!isNewer) return;
  if (now - last < THROTTLE_MS) return;

  lastAppliedAt.set(update.truckId, now);
  states = new Map(states);
  states.set(update.truckId, update);
  emit();
}

/** Bulk replace all truck states (e.g. from OnFleetUpdate after reconnect). */
export function applyFleetUpdate(updates: TruckState[]): void {
  const next = new Map<string, TruckState>();
  for (const u of updates) {
    next.set(u.truckId, u);
    lastAppliedAt.set(u.truckId, Date.now());
  }
  states = next;
  emit();
}

/**
 * Apply an OnPresenceChange (protocol §3.3) transition without touching the
 * truck's last known position, so an offline truck is greyed out rather than
 * dropped from the map (§3.8).
 */
export function markTruckOffline(truckId: string): void {
  const existing = states.get(truckId);
  if (!existing) return;
  states = new Map(states);
  states.set(truckId, { ...existing, isOnline: false, isMoving: false });
  emit();
}

/**
 * Reverse transition. The BFF only pushes explicit offline events — a truck
 * coming back online normally arrives via OnTruckStateUpdate — but the event
 * carries a boolean, so the client honours both values.
 */
export function markTruckOnline(truckId: string): void {
  const existing = states.get(truckId);
  if (!existing || existing.isOnline) return;
  states = new Map(states);
  states.set(truckId, { ...existing, isOnline: true });
  emit();
}

/** Clear all states (e.g. on logout). */
export function clearTruckStates(): void {
  states = new Map();
  lastAppliedAt.clear();
  emit();
}

// ─── React hook ───────────────────────────────────────────────────

export function useTruckStates(): TruckStateMap {
  return useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
}

export function useTruckStateMap(truckId: string): TruckState | undefined {
  const map = useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
  return map.get(truckId);
}
