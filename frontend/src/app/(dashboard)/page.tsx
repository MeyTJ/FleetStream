"use client";

import { useAuth } from "@/lib/auth-context";
import { Map } from "lucide-react";
import { FleetSummaryCards } from "@/components/fleet-summary-cards";

export default function DashboardPage() {
  const { subject } = useAuth();

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight">
          Welcome, {subject}
        </h1>
        <p className="text-muted-foreground">
          Fleet-wide operational overview. Data refreshes every 10 seconds.
        </p>
      </div>

      {/* Fleet summary KPI cards — wired to BFF */}
      <FleetSummaryCards />

      {/* Quick-actions / placeholder for future F2 map widget */}
      <div className="rounded-lg border border-dashed p-12 text-center">
        <Map className="mx-auto h-10 w-10 text-muted-foreground/50" />
        <h3 className="mt-4 text-lg font-semibold">Live Map</h3>
        <p className="mt-1 text-sm text-muted-foreground">
          Real-time map with truck markers — coming in F2.
        </p>
      </div>
    </div>
  );
}
