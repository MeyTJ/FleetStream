"use client";

import { useState, useCallback } from "react";
import dynamic from "next/dynamic";
import { SignalRProvider } from "@/lib/signalr-provider";
import { useSignalREvents } from "@/lib/hooks/signalr-events";
import { ReconnectBanner } from "@/components/reconnect-banner";
import { TruckDetailPanel } from "@/components/truck-detail-panel";
import { Skeleton } from "@/components/skeleton";
import { AlertToasts } from "@/components/alert-toast";

// Lazy-load the map to avoid SSR issues with maplibre-gl
const FleetMap = dynamic(
  () => import("@/components/fleet-map").then((m) => m.FleetMap),
  {
    ssr: false,
    loading: () => (
      <div className="flex h-full items-center justify-center">
        <Skeleton className="h-8 w-48" />
      </div>
    ),
  },
);

export default function MapPage() {
  return (
    <SignalRProvider>
      <MapPageInner />
    </SignalRProvider>
  );
}

function MapPageInner() {
  const [selectedTruckId, setSelectedTruckId] = useState<string | null>(null);

  // Mount SignalR event handlers
  useSignalREvents();

  const handleTruckClick = useCallback((truckId: string) => {
    setSelectedTruckId((prev) => (prev === truckId ? null : truckId));
  }, []);

  const handleClose = useCallback(() => {
    setSelectedTruckId(null);
  }, []);

  return (
    <div className="flex h-[calc(100vh-theme(spacing.24))] flex-col overflow-hidden rounded-lg border">
      <ReconnectBanner />

      <div className="flex flex-1 overflow-hidden">
        {/* Map area */}
        <div className="flex-1">
          <FleetMap
            onTruckClick={handleTruckClick}
            selectedTruckId={selectedTruckId}
          />
        </div>

        {/* Detail panel */}
        {selectedTruckId && (
          <div className="w-80 shrink-0">
            <TruckDetailPanel
              truckId={selectedTruckId}
              onClose={handleClose}
            />
          </div>
        )}
      </div>
      <AlertToasts />
    </div>
  );
}

