"use client";

import { useParams } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, Truck, Gauge, Thermometer, Fuel, AlertTriangle, MapPin } from "lucide-react";
import { useTruck, useTruckState } from "@/lib/hooks/fleet";
import { StatusBadge } from "@/components/truck-table";
import { Skeleton } from "@/components/skeleton";
import { ErrorState } from "@/components/error-state";
import { TelemetrySection } from "@/components/telemetry-sparkline";
import type { RiskLevel } from "@/lib/types";
import { cn } from "@/lib/utils";

const riskBadge: Record<RiskLevel, string> = {
  Low: "bg-green-100 text-green-800 dark:bg-green-950 dark:text-green-300",
  Medium: "bg-yellow-100 text-yellow-800 dark:bg-yellow-950 dark:text-yellow-300",
  High: "bg-orange-100 text-orange-800 dark:bg-orange-950 dark:text-orange-300",
  Critical: "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-300",
};

function DetailSkeleton() {
  return (
    <div className="space-y-4">
      <Skeleton className="h-7 w-48" />
      <Skeleton className="h-4 w-32" />
      <div className="grid gap-4 sm:grid-cols-2">
        {Array.from({ length: 6 }).map((_, i) => (
          <div key={i} className="rounded-lg border p-4">
            <Skeleton className="h-3 w-20" />
            <Skeleton className="mt-2 h-6 w-32" />
          </div>
        ))}
      </div>
    </div>
  );
}

export default function TruckDetailPage() {
  const params = useParams<{ truckId: string }>();
  const truckId = params.truckId;
  const { data: truck, isLoading, error, refetch } = useTruck(truckId);
  const { data: liveState } = useTruckState(truckId);

  if (isLoading) return <DetailSkeleton />;

  if (error) {
    return (
      <div className="space-y-4">
        <Link href="/trucks" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="h-4 w-4" /> Back to trucks
        </Link>
        <ErrorState error={error} reset={() => refetch()} />
      </div>
    );
  }

  if (!truck) return null;

  return (
    <div className="space-y-6">
      <Link href="/trucks" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeft className="h-4 w-4" /> Back to trucks
      </Link>

      <div className="flex items-center gap-3">
        <Truck className="h-6 w-6 text-muted-foreground" />
        <div>
          <h1 className="text-2xl font-bold tracking-tight">{truck.name}</h1>
          <p className="text-muted-foreground">{truck.licensePlate}</p>
        </div>
        <StatusBadge status={truck.status} />
      </div>

      {/* Truck metadata */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <MetaCard label="Truck ID" value={<span className="font-mono text-xs">{truck.id}</span>} />
        <MetaCard label="Status" value={truck.status} />
        <MetaCard label="Last Updated" value={new Date(truck.updatedAt).toLocaleString()} />
        <MetaCard label="Created" value={new Date(truck.createdAt).toLocaleString()} />
      </div>

      {/* Live state from BFF REST */}
      {liveState ? (
        <LiveStateSection liveState={liveState} />
      ) : (
        <div className="rounded-lg border border-dashed p-8 text-center">
          <Truck className="mx-auto h-8 w-8 text-muted-foreground/50" />
          <h3 className="mt-3 text-sm font-semibold">Live State</h3>
          <p className="mt-1 text-xs text-muted-foreground">
            No live telemetry yet. Data appears once the truck reports telemetry.
          </p>
        </div>
      )}

      {/* Telemetry history (24 h window) */}
      <TelemetrySection truckId={truck.id} />
    </div>
  );
}

function MetaCard({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="rounded-lg border bg-white p-4 dark:bg-zinc-900">
      <p className="text-xs font-medium text-muted-foreground">{label}</p>
      <div className="mt-1 text-sm font-medium">{value}</div>
    </div>
  );
}

function LiveStateSection({ liveState }: { liveState: NonNullable<ReturnType<typeof useTruckState>["data"]> }) {
  return (
    <div className="space-y-4">
      <h2 className="text-lg font-semibold">Live State</h2>
      <div className="flex flex-wrap gap-2">
        <span className={cn("rounded-full px-2 py-0.5 text-xs font-medium", riskBadge[liveState.riskLevel])}>
          Risk: {liveState.riskLevel} ({liveState.riskScore.toFixed(1)})
        </span>
        <span className={cn("rounded-full px-2 py-0.5 text-xs font-medium", liveState.isOnline ? "bg-green-50 text-green-700" : "bg-zinc-100 text-zinc-500")}>
          {liveState.isOnline ? "Online" : "Offline"}
        </span>
        <span className={cn("rounded-full px-2 py-0.5 text-xs font-medium", liveState.isMoving ? "bg-blue-50 text-blue-700" : "bg-zinc-100 text-zinc-500")}>
          {liveState.isMoving ? "Moving" : "Stopped"}
        </span>
      </div>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <MetaCard label="Speed" value={<span className="flex items-center gap-1"><Gauge className="h-3.5 w-3.5 text-muted-foreground" /> {liveState.speedKmh.toFixed(0)} km/h</span>} />
        <MetaCard label="Engine Temp" value={<span className="flex items-center gap-1"><Thermometer className="h-3.5 w-3.5 text-muted-foreground" /> {liveState.engineTemperatureCelsius.toFixed(0)}°C</span>} />
        <MetaCard label="Fuel Level" value={<span className="flex items-center gap-1"><Fuel className="h-3.5 w-3.5 text-muted-foreground" /> {liveState.fuelLevelPercent.toFixed(1)}%</span>} />
        <MetaCard label="Risk Score" value={<span className="flex items-center gap-1"><AlertTriangle className="h-3.5 w-3.5 text-muted-foreground" /> {liveState.riskScore.toFixed(1)}</span>} />
      </div>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <MetaCard label="Position" value={<span className="flex items-center gap-1 font-mono text-xs"><MapPin className="h-3.5 w-3.5 text-muted-foreground" /> {liveState.latitude.toFixed(4)}, {liveState.longitude.toFixed(4)}</span>} />
        <MetaCard label="Distance" value={`${liveState.totalDistanceKm.toFixed(1)} km`} />
        <MetaCard label="Violations" value={String(liveState.violationsCount)} />
        <MetaCard label="Anomalies" value={String(liveState.anomaliesCount)} />
      </div>
      <p className="text-xs text-muted-foreground">
        Last update: {new Date(liveState.timestamp).toLocaleString()}
      </p>
    </div>
  );
}
