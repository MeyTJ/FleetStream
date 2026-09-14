"use client";

/**
 * Hook that subscribes to SignalR hub events and pushes them into
 * the truck-state-store and alert-store. Mount once at the map/dashboard level.
 */

import { useEffect, useRef } from "react";
import { useSignalR } from "@/lib/signalr-provider";
import type { TruckState, Alert, TruckTelemetry } from "@/lib/types";
import {
  applyTruckState,
  applyFleetUpdate,
  clearTruckStates,
} from "@/lib/truck-state-store";
import { pushAlert, clearAlerts, purgeAlerts } from "@/lib/alert-store";
import {
  applyTelemetrySample,
  clearTelemetrySamples,
} from "@/lib/telemetry-sample-store";
import { useAuth } from "@/lib/auth-context";

export function useSignalREvents() {
  const { connection, status } = useSignalR();
  const { token } = useAuth();
  const registeredRef = useRef(false);

  // Register event handlers when connection is available
  useEffect(() => {
    if (!connection || registeredRef.current) return;

    connection.on("OnTruckStateUpdate", (state: TruckState) => {
      applyTruckState(state);
    });

    connection.on("OnFleetUpdate", (states: TruckState[]) => {
      applyFleetUpdate(states);
    });

    connection.on("OnAlert", (alert: Alert) => {
      pushAlert(alert);
    });

    // SignalR protocol §3.3 — the high-rate admin telemetry stream. The server
    // adds `fleet:admin` connections to `telemetry:full` in OnConnectedAsync,
    // so non-admin sessions simply never receive this event and the buffers
    // stay empty (consumers fall back to the 24 h REST history).
    connection.on("OnTelemetrySample", (sample: TruckTelemetry) => {
      applyTelemetrySample(sample);
    });

    // SignalR protocol §3.3 — server-side ring-buffer eviction. Payload:
    // OnAlertsPurged(count: number, beforeTimestamp: string).
    // Trim entries older than the cutoff; the alert-store records a
    // purge notice that the AlertsPurgeBanner renders.
    connection.on(
      "OnAlertsPurged",
      (count: number, beforeTimestamp: string) => {
        purgeAlerts(beforeTimestamp, count);
      },
    );

    registeredRef.current = true;

    return () => {
      if (registeredRef.current) {
        connection.off("OnTruckStateUpdate");
        connection.off("OnFleetUpdate");
        connection.off("OnAlert");
        connection.off("OnTelemetrySample");
        connection.off("OnAlertsPurged");
        registeredRef.current = false;
      }
    };
  }, [connection]);

  // Clear stores on logout
  useEffect(() => {
    if (!token) {
      clearTruckStates();
      clearAlerts();
      clearTelemetrySamples();
    }
  }, [token]);

  return { status };
}
