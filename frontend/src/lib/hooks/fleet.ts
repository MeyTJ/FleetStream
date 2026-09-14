/**
 * Fleet data hooks using TanStack Query.
 *
 * All hooks hit the BFF REST endpoints.
 * Query keys are namespaced by resource for cache management.
 */

import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiGet, apiPost } from "@/lib/api-client";
import type {
  FleetSummary,
  Truck,
  CursorPage,
  TruckState,
  TruckStatus,
  TruckTelemetry,
  Alert,
  AlertSeverity,
} from "@/lib/types";

// ─── Query keys ───────────────────────────────────────────────────

export const fleetKeys = {
  all: ["fleet"] as const,
  summary: () => [...fleetKeys.all, "summary"] as const,
  trucks: () => [...fleetKeys.all, "trucks"] as const,
  truckList: (params: TruckListParams) =>
    [...fleetKeys.trucks(), params] as const,
  truck: (truckId: string) => [...fleetKeys.all, "truck", truckId] as const,
  truckState: (truckId: string) =>
    [...fleetKeys.all, "truck", truckId, "state"] as const,
  truckTelemetry: (
    truckId: string,
    params: Pick<TruckTelemetryParams, "hours" | "limit">,
  ) =>
    [...fleetKeys.all, "truck", truckId, "telemetry", params] as const,
};

// ─── Fleet Summary ────────────────────────────────────────────────

export function useFleetSummary() {
  return useQuery({
    queryKey: fleetKeys.summary(),
    queryFn: () => apiGet<FleetSummary>("/api/v1/fleet/summary"),
  });
}

// ─── Truck List ───────────────────────────────────────────────────

export interface TruckListParams {
  cursor?: string;
  pageSize?: number;
  status?: TruckStatus;
}

export function useTrucks(params: TruckListParams = {}) {
  return useQuery({
    queryKey: fleetKeys.truckList(params),
    queryFn: () =>
      apiGet<CursorPage<Truck>>("/api/v1/fleet/trucks", {
        cursor: params.cursor,
        pageSize: params.pageSize ?? 50,
        status: params.status,
      }),
    placeholderData: (prev) => prev, // Keep previous data while fetching next page
  });
}

// ─── Single Truck ─────────────────────────────────────────────────

export function useTruck(truckId: string) {
  return useQuery({
    queryKey: fleetKeys.truck(truckId),
    queryFn: () => apiGet<Truck>(`/api/v1/fleet/trucks/${truckId}`),
    enabled: !!truckId,
  });
}

// ─── Truck State ──────────────────────────────────────────────────

export function useTruckState(truckId: string) {
  return useQuery({
    queryKey: fleetKeys.truckState(truckId),
    queryFn: () =>
      apiGet<TruckState>(`/api/v1/fleet/trucks/${truckId}/state`),
    enabled: !!truckId,
  });
}

// ─── Truck Telemetry ─────────────────────────────────────────────

export interface TruckTelemetryParams {
  /** Hours back from now (clamped ≤ 24 by the BFF contract). Default 24. */
  hours?: number;
  /** Max samples (1–1000 per contract). Default 200. */
  limit?: number;
}

/**
 * Recent telemetry history for a truck
 * (`GET /api/v1/fleet/trucks/{id}/telemetry`, newest-first array).
 *
 * The window is computed inside queryFn so the cached key stays stable
 * while each refetch slides the window forward.
 */
export function useTruckTelemetry(
  truckId: string,
  params: TruckTelemetryParams = {},
) {
  const { hours = 24, limit = 200 } = params;

  return useQuery({
    queryKey: fleetKeys.truckTelemetry(truckId, { hours, limit }),
    queryFn: () => {
      const to = new Date();
      const from = new Date(to.getTime() - hours * 3_600_000);
      return apiGet<TruckTelemetry[]>(
        `/api/v1/fleet/trucks/${encodeURIComponent(truckId)}/telemetry`,
        { from: from.toISOString(), to: to.toISOString(), limit },
      );
    },
    enabled: !!truckId,
  });
}

// ─── Alerts ───────────────────────────────────────────────────

export interface AlertListParams {
  cursor?: string;
  pageSize?: number;
  severity?: AlertSeverity | string;
  truckId?: string;
  onlyActive?: boolean;
}

export function useAlerts(params: AlertListParams = {}) {
  return useQuery({
    queryKey: ["fleet", "alerts", params],
    queryFn: () =>
      apiGet<CursorPage<Alert>>("/api/v1/fleet/alerts", {
        cursor: params.cursor,
        pageSize: params.pageSize ?? 100,
        severity: params.severity,
        truckId: params.truckId,
        onlyActive: params.onlyActive ?? true,
      }),
    placeholderData: (prev) => prev,
  });
}

export function useAcknowledgeAlert() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      alertId,
      acknowledgedBy,
    }: {
      alertId: string;
      acknowledgedBy: string;
    }) =>
      apiPost<void>(`/api/v1/fleet/alerts/${alertId}/acknowledge`, {
        acknowledgedBy,
      }),
    onSuccess: () => {
      // Invalidate alert queries to refetch
      void queryClient.invalidateQueries({ queryKey: ["fleet", "alerts"] });
    },
  });
}
