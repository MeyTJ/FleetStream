"use client";

import { X, Gauge, Thermometer, Fuel, AlertTriangle } from "lucide-react";
import { useTruckStateMap } from "@/lib/truck-state-store";
import { StatusBadge } from "@/components/truck-table";
import { TelemetrySparkline } from "@/components/telemetry-sparkline";
import type { TruckStatus, RiskLevel } from "@/lib/types";
import { cn } from "@/lib/utils";
import { useSignalR } from "@/lib/signalr-provider";
import { useEffect } from "react";

const riskBadge: Record<RiskLevel, string> = {
  Low: "bg-green-100 text-green-800 dark:bg-green-950 dark:text-green-300",
  Medium:
    "bg-yellow-100 text-yellow-800 dark:bg-yellow-950 dark:text-yellow-300",
  High:
    "bg-orange-100 text-orange-800 dark:bg-orange-950 dark:text-orange-300",
  Critical: "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-300",
};

interface TruckDetailPanelProps {
  truckId: string;
  truckName?: string;
  truckPlate?: string;
  truckStatus?: TruckStatus;
  onClose: () => void;
}

export function TruckDetailPanel({
  truckId,
  truckName,
  truckPlate,
  truckStatus,
  onClose,
}: TruckDetailPanelProps) {
  const truck = useTruckStateMap(truckId);
  const { joinTruckGroup, leaveTruckGroup } = useSignalR();

  // Join truck-specific group for granular updates. Routed through the provider
  // so the membership survives a reconnect (protocol §3.2).
  useEffect(() => {
    joinTruckGroup(truckId);
    return () => {
      leaveTruckGroup(truckId);
    };
  }, [joinTruckGroup, leaveTruckGroup, truckId]);

  return (
    <div className="flex h-full flex-col border-l bg-white dark:bg-zinc-900">
      {/* Header */}
      <div className="flex items-center justify-between border-b px-4 py-3">
        <div>
          <h2 className="text-sm font-semibold">
            {truckName ?? truckId}
          </h2>
          {truckPlate && (
            <p className="font-mono text-xs text-muted-foreground">
              {truckPlate}
            </p>
          )}
        </div>
        <button
          onClick={onClose}
          className="rounded-md p-1 transition-colors hover:bg-zinc-100 dark:hover:bg-zinc-800"
          aria-label="Close detail panel"
        >
          <X className="h-4 w-4" />
        </button>
      </div>

      {/* Content */}
      {truck ? (
        <div className="flex-1 overflow-y-auto p-4">
          {/* Status badges */}
          <div className="mb-4 flex flex-wrap gap-2">
            {truckStatus && <StatusBadge status={truckStatus} />}
            <span
              className={cn(
                "inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium",
                riskBadge[truck.riskLevel],
              )}
            >
              Risk: {truck.riskLevel}
            </span>
            <span
              className={cn(
                "inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium",
                truck.isOnline
                  ? "bg-green-50 text-green-700"
                  : "bg-zinc-100 text-zinc-500",
              )}
            >
              {truck.isOnline ? "Online" : "Offline"}
            </span>
            <span
              className={cn(
                "inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium",
                truck.isMoving
                  ? "bg-blue-50 text-blue-700"
                  : "bg-zinc-100 text-zinc-500",
              )}
            >
              {truck.isMoving ? "Moving" : "Stopped"}
            </span>
          </div>

          {/* Metric cards */}
          <div className="grid grid-cols-2 gap-3">
            <MetricCard
              icon={Gauge}
              label="Speed"
              value={`${truck.speedKmh.toFixed(0)} km/h`}
            />
            <MetricCard
              icon={Thermometer}
              label="Engine Temp"
              value={`${truck.engineTemperatureCelsius.toFixed(0)}°C`}
            />
            <MetricCard
              icon={Fuel}
              label="Fuel"
              value={`${truck.fuelLevelPercent.toFixed(1)}%`}
            />
            <MetricCard
              icon={AlertTriangle}
              label="Risk Score"
              value={truck.riskScore.toFixed(1)}
            />
          </div>

          {/* Additional data */}
          <div className="mt-4 space-y-2 rounded-lg border p-3">
            <Row label="Distance" value={`${truck.totalDistanceKm.toFixed(1)} km`} />
            <Row label="Violations" value={String(truck.violationsCount)} />
            <Row label="Anomalies" value={String(truck.anomaliesCount)} />
            <Row label="Last Update" value={new Date(truck.timestamp).toLocaleString()} />
          </div>

          {/* Speed sparkline (24 h telemetry history) */}
          <div className="mt-4">
            <TelemetrySparkline truckId={truckId} metric="speedKmh" />
          </div>
        </div>
      ) : (
        <div className="flex flex-1 items-center justify-center p-4">
          <p className="text-sm text-muted-foreground">
            Waiting for live state data…
          </p>
        </div>
      )}
    </div>
  );
}

function MetricCard({
  icon: Icon,
  label,
  value,
}: {
  icon: React.ComponentType<{ className?: string }>;
  label: string;
  value: string;
}) {
  return (
    <div className="rounded-lg border bg-zinc-50 p-3 dark:bg-zinc-800">
      <div className="flex items-center gap-1.5">
        <Icon className="h-3.5 w-3.5 text-muted-foreground" />
        <span className="text-xs text-muted-foreground">{label}</span>
      </div>
      <p className="mt-1 text-sm font-semibold tabular-nums">{value}</p>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between text-sm">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-medium">{value}</span>
    </div>
  );
}
