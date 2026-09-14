import { act, renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import {
  pushAlert,
  replaceAlerts,
  purgeAlerts,
  clearAlerts,
  optimisticAck,
  rollbackAck,
  useAlerts,
  useUnackedAlertCount,
} from "./alert-store";
import {
  applyTruckState,
  applyFleetUpdate,
  clearTruckStates,
  markTruckOffline,
  useTruckStates,
} from "./truck-state-store";
import {
  applyTelemetrySample,
  clearTelemetrySamples,
  useTelemetrySamples,
  useHasLiveTelemetry,
} from "./telemetry-sample-store";
import type { Alert, TruckState, TruckTelemetry } from "./types";

function makeAlert(overrides: Partial<Alert> = {}): Alert {
  return {
    id: "alert-1",
    truckId: "truck-1",
    alertType: "SpeedViolation",
    severity: "Warning",
    message: "Overspeed",
    timestamp: new Date().toISOString(),
    isAcknowledged: false,
    acknowledgedBy: null,
    acknowledgedAt: null,
    ...overrides,
  };
}

function makeTruckState(overrides: Partial<TruckState> = {}): TruckState {
  return {
    truckId: "truck-1",
    timestamp: new Date().toISOString(),
    latitude: 50,
    longitude: 10,
    speedKmh: 80,
    engineTemperatureCelsius: 90,
    fuelLevelPercent: 50,
    isMoving: true,
    isOnline: true,
    riskLevel: "Low",
    riskScore: 1,
    totalDistanceKm: 100,
    violationsCount: 0,
    anomaliesCount: 0,
    ...overrides,
  };
}

function makeSample(overrides: Partial<TruckTelemetry> = {}): TruckTelemetry {
  return {
    truckId: "t1",
    eventTimestamp: new Date().toISOString(),
    latitude: 0,
    longitude: 0,
    speedKmh: 0,
    engineTemperatureCelsius: 0,
    fuelLevelPercent: 0,
    speedViolation: false,
    tempAnomaly: false,
    fuelLow: false,
    geofenceViolation: false,
    riskLevel: "Low",
    riskScore: 0,
    ...overrides,
  };
}

beforeEach(() => {
  clearAlerts();
  clearTruckStates();
  clearTelemetrySamples();
});

describe("alert-store ring buffer", () => {
  it("caps the feed at 500 entries, newest first", () => {
    // Protocol §3.3 pins the 500-alert buffer; the virtualized feed renders
    // against this bound.
    const { result } = renderHook(() => useAlerts());
    act(() => {
      for (let i = 0; i < 520; i++) {
        pushAlert(
          makeAlert({
            id: `a-${i}`,
            timestamp: new Date(1_700_000_000_000 + i * 1000).toISOString(),
          }),
        );
      }
    });
    expect(result.current).toHaveLength(500);
    expect(result.current[0].id).toBe("a-519");
  });

  it("updates an existing alert in place rather than duplicating it", () => {
    const { result } = renderHook(() => useAlerts());
    act(() => {
      pushAlert(makeAlert({ id: "dup" }));
      pushAlert(makeAlert({ id: "dup", isAcknowledged: true }));
    });
    expect(result.current.filter((a) => a.id === "dup")).toHaveLength(1);
    expect(result.current[0].isAcknowledged).toBe(true);
  });

  it("rolls back an optimistic ack", () => {
    const alerts = renderHook(() => useAlerts());
    const unacked = renderHook(() => useUnackedAlertCount());
    act(() => pushAlert(makeAlert({ id: "ack-1" })));

    let previous: Alert | undefined;
    act(() => {
      previous = optimisticAck("ack-1", "operator");
    });
    expect(alerts.result.current[0].isAcknowledged).toBe(true);
    expect(unacked.result.current).toBe(0);

    act(() => {
      if (previous) rollbackAck("ack-1", previous);
    });
    expect(alerts.result.current[0].isAcknowledged).toBe(false);
    expect(unacked.result.current).toBe(1);
  });

  it("drops entries older than the server purge cutoff", () => {
    const { result } = renderHook(() => useAlerts());
    const old = new Date(1_000_000_000_000).toISOString();
    const recent = new Date(2_000_000_000_000).toISOString();
    act(() => {
      replaceAlerts([
        makeAlert({ id: "stale", timestamp: old }),
        makeAlert({ id: "fresh", timestamp: recent }),
      ]);
    });

    act(() => purgeAlerts(recent, 7));
    expect(result.current.map((a) => a.id)).toEqual(["fresh"]);
  });

  it("ignores a purge cutoff that is not a valid date", () => {
    const { result } = renderHook(() => useAlerts());
    act(() => replaceAlerts([makeAlert({ id: "keep" })]));
    let removed = 1;
    act(() => {
      removed = purgeAlerts("not-a-date");
    });
    expect(removed).toBe(0);
    expect(result.current).toHaveLength(1);
  });
});

describe("truck-state-store", () => {
  it("rejects an update older than the state already held", () => {
    const { result } = renderHook(() => useTruckStates());
    act(() => {
      applyTruckState(
        makeTruckState({
          truckId: "t1",
          timestamp: new Date(2_000_000_000_000).toISOString(),
          speedKmh: 99,
        }),
      );
    });
    act(() => {
      applyTruckState(
        makeTruckState({
          truckId: "t1",
          timestamp: new Date(1_000_000_000_000).toISOString(),
          speedKmh: 10,
        }),
      );
    });
    expect(result.current.get("t1")?.speedKmh).toBe(99);
  });

  it("bulk-replaces the fleet on OnFleetUpdate", () => {
    const { result } = renderHook(() => useTruckStates());
    act(() => applyTruckState(makeTruckState({ truckId: "gone" })));
    act(() => {
      applyFleetUpdate([
        makeTruckState({ truckId: "a" }),
        makeTruckState({ truckId: "b" }),
      ]);
    });
    expect(result.current.has("gone")).toBe(false);
    expect([...result.current.keys()].sort()).toEqual(["a", "b"]);
  });

  it("marks a truck offline while keeping its last known position", () => {
    const { result } = renderHook(() => useTruckStates());
    act(() => applyTruckState(makeTruckState({ truckId: "t9" })));
    act(() => markTruckOffline("t9"));
    const truck = result.current.get("t9");
    expect(truck?.isOnline).toBe(false);
    expect(truck?.latitude).toBe(50);
  });
});

describe("telemetry-sample-store", () => {
  it("keeps a bounded ring of the newest samples per truck", () => {
    const { result } = renderHook(() => useTelemetrySamples("t1"));
    act(() => {
      for (let i = 0; i < 150; i++) {
        applyTelemetrySample(
          makeSample({
            truckId: "t1",
            eventTimestamp: new Date(
              1_000_000_000_000 + i * 5_000,
            ).toISOString(),
            speedKmh: i,
          }),
        );
      }
    });
    expect(result.current).toHaveLength(120);
    expect(result.current[result.current.length - 1].speedKmh).toBe(149);
    // Oldest evicted: the head is sample 30, not 0.
    expect(result.current[0].speedKmh).toBe(30);
  });

  it("reports no live stream for a non-admin session", () => {
    const hasLive = renderHook(() => useHasLiveTelemetry());
    const other = renderHook(() => useTelemetrySamples("t2"));
    expect(hasLive.result.current).toBe(false);
    expect(other.result.current).toEqual([]);

    act(() => applyTelemetrySample(makeSample({ truckId: "t1" })));
    expect(hasLive.result.current).toBe(true);
    // Another truck's ring is untouched and keeps a stable reference.
    expect(other.result.current).toEqual([]);
  });
});
