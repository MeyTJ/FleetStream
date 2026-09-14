"use client";

/**
 * Live telemetry-sample store — the client side of `OnTelemetrySample`
 * (BFF SignalR protocol §3.3).
 *
 * The server pushes a sample per truck at most every 5 s, and only to members
 * of the `telemetry:full` group (granted server-side from the `fleet:admin`
 * role — see FleetHub.OnConnectedAsync). Non-admin connections simply never
 * receive the event, so this store stays empty and consumers fall back to the
 * 24 h REST history from `GET /api/v1/fleet/trucks/{id}/telemetry`.
 *
 * Each truck keeps a bounded ring of the most recent samples so the detail
 * panel can draw a live sparkline without unbounded memory growth.
 */

import { useSyncExternalStore } from "react";
import type { TruckTelemetry } from "@/lib/types";

const MAX_SAMPLES_PER_TRUCK = 120;

type SampleMap = Map<string, TruckTelemetry[]>;

let samples: SampleMap = new Map();
let listeners: Array<() => void> = [];

function emit(): void {
  for (const listener of listeners) listener();
}

function subscribe(listener: () => void): () => void {
  listeners = [...listeners, listener];
  return () => {
    listeners = listeners.filter((l) => l !== listener);
  };
}

function getSnapshot(): SampleMap {
  return samples;
}

/** Append one pushed sample, evicting the oldest beyond the ring capacity. */
export function applyTelemetrySample(sample: TruckTelemetry): void {
  const existing = samples.get(sample.truckId);
  const next = existing ? existing.slice() : [];
  next.push(sample);
  if (next.length > MAX_SAMPLES_PER_TRUCK) {
    next.splice(0, next.length - MAX_SAMPLES_PER_TRUCK);
  }
  samples = new Map(samples);
  samples.set(sample.truckId, next);
  emit();
}

/** Drop all buffered samples (e.g. on logout). */
export function clearTelemetrySamples(): void {
  if (samples.size === 0) return;
  samples = new Map();
  emit();
}

/** Oldest → newest live samples for one truck; empty for non-admin sessions. */
export function useTelemetrySamples(truckId: string): TruckTelemetry[] {
  const map = useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
  // Only the pushed truck's array is replaced on each write, so other trucks
  // keep a stable reference and do not re-render.
  return map.get(truckId) ?? EMPTY;
}

const EMPTY: TruckTelemetry[] = [];

/** True when this session has ever received a live sample (i.e. admin stream). */
export function useHasLiveTelemetry(): boolean {
  const map = useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
  return map.size > 0;
}
