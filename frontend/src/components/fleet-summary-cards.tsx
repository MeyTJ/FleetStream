"use client";

import {
  Truck,
  Activity,
  Gauge,
  AlertTriangle,
  MapPin,
  Fuel,
} from "lucide-react";
import { useFleetSummary } from "@/lib/hooks/fleet";
import { Skeleton } from "@/components/skeleton";
import { ErrorState } from "@/components/error-state";

function StatCard({
  label,
  value,
  icon: Icon,
  description,
  accent,
}: {
  label: string;
  value: string | number;
  icon: React.ComponentType<{ className?: string }>;
  description: string;
  accent?: string;
}) {
  return (
    <div className="rounded-lg border bg-white p-5 shadow-sm dark:bg-zinc-900">
      <div className="flex items-center justify-between">
        <p className="text-sm font-medium text-muted-foreground">{label}</p>
        <Icon className={`h-4 w-4 ${accent ?? "text-muted-foreground"}`} />
      </div>
      <p className="mt-2 text-2xl font-bold tabular-nums">
        {typeof value === "number" ? value.toLocaleString() : value}
      </p>
      <p className="mt-1 text-xs text-muted-foreground">{description}</p>
    </div>
  );
}

function SummaryCardSkeleton() {
  return (
    <div className="rounded-lg border bg-white p-5 shadow-sm dark:bg-zinc-900">
      <Skeleton className="h-4 w-24" />
      <Skeleton className="mt-3 h-8 w-20" />
      <Skeleton className="mt-2 h-3 w-32" />
    </div>
  );
}

export function FleetSummaryCards() {
  const { data, isLoading, error, refetch } = useFleetSummary();

  if (isLoading) {
    return (
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {Array.from({ length: 6 }).map((_, i) => (
          <SummaryCardSkeleton key={i} />
        ))}
      </div>
    );
  }

  if (error) {
    return <ErrorState error={error} reset={() => refetch()} />;
  }

  if (!data) return null;

  return (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <StatCard
        label="Total Trucks"
        value={data.totalTrucks}
        icon={Truck}
        description="Fleet-wide truck count"
      />
      <StatCard
        label="Online"
        value={data.onlineTrucks}
        icon={Activity}
        description="Currently reporting telemetry"
        accent="text-green-600"
      />
      <StatCard
        label="Moving"
        value={data.movingTrucks}
        icon={Gauge}
        description="In transit"
        accent="text-blue-600"
      />
      <StatCard
        label="At Risk"
        value={data.atRiskTrucks}
        icon={AlertTriangle}
        description="Trucks flagged as at-risk"
        accent={
          data.atRiskTrucks > 0
            ? "text-red-600"
            : "text-muted-foreground"
        }
      />
      <StatCard
        label="Avg. Speed"
        value={`${data.averageSpeed.toFixed(1)} km/h`}
        icon={MapPin}
        description="Fleet-wide average speed"
      />
      <StatCard
        label="Avg. Fuel"
        value={`${data.averageFuelLevel.toFixed(1)}%`}
        icon={Fuel}
        description="Fleet-wide average fuel level"
      />
    </div>
  );
}
