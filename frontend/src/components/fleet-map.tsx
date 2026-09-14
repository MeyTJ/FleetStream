"use client";

import { useEffect, useRef, useState } from "react";
import * as maplibregl from "maplibre-gl";
import type { Map as MapLibreMap, Marker } from "maplibre-gl";
import "maplibre-gl/dist/maplibre-gl.css";
import { useTruckStates } from "@/lib/truck-state-store";
import type { TruckState, RiskLevel } from "@/lib/types";

const riskColors: Record<RiskLevel, string> = {
  Low: "#22c55e",
  Medium: "#f59e0b",
  High: "#f97316",
  Critical: "#ef4444",
};

function createMarkerEl(
  truck: TruckState,
  onActivate: () => void,
): HTMLDivElement {
  const el = document.createElement("div");
  el.className = "truck-marker";
  el.setAttribute("role", "button");
  el.setAttribute("tabindex", "0");
  el.setAttribute("aria-label", `Truck ${truck.truckId} — ${truck.riskLevel}`);
  const color = truck.isOnline ? riskColors[truck.riskLevel] : "#71717a";
  el.style.cssText =
    `width:12px;height:12px;border-radius:50%;` +
    `background:${color};border:2px solid ${truck.isOnline ? "#fff" : "#a1a1aa"};` +
    `cursor:pointer;transition:transform .15s;box-shadow:0 1px 3px rgba(0,0,0,.3)`;
  el.addEventListener("mouseenter", () => (el.style.transform = "scale(1.5)"));
  el.addEventListener("mouseleave", () => (el.style.transform = "scale(1)"));
  // Keyboard users get the same visual focus indication as hover
  el.addEventListener("focus", () => (el.style.transform = "scale(1.5)"));
  el.addEventListener("blur", () => (el.style.transform = "scale(1)"));
  el.addEventListener("click", onActivate);
  el.addEventListener("keydown", (e) => {
    if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      onActivate();
    }
  });
  return el;
}

interface FleetMapProps {
  onTruckClick?: (truckId: string) => void;
  selectedTruckId?: string | null;
}

export function FleetMap({ onTruckClick, selectedTruckId }: FleetMapProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<MapLibreMap | null>(null);
  const markersRef = useRef<Map<string, Marker>>(new Map());
  const truckStates = useTruckStates();
  const [mapReady, setMapReady] = useState(false);

  // Initialize map once
  useEffect(() => {
    if (!containerRef.current || mapRef.current) return;
    const map = new maplibregl.Map({
      container: containerRef.current,
      style: "https://demotiles.maplibre.org/style.json",
      center: [10, 50],
      zoom: 4,
    });
    map.addControl(new maplibregl.NavigationControl(), "top-right");
    map.on("load", () => setMapReady(true));
    mapRef.current = map;
    // Capture ref at effect start for safe cleanup
    const markers = markersRef.current;
    return () => {
      map.remove();
      mapRef.current = null;
      markers.clear();
      setMapReady(false);
    };
  }, []);

  // Sync markers with truck states
  useEffect(() => {
    if (!mapReady || !mapRef.current) return;
    const map = mapRef.current;
    const cur = markersRef.current;
    const seen = new Set<string>();

    truckStates.forEach((truck, truckId) => {
      seen.add(truckId);
      const existing = cur.get(truckId);
      if (existing) {
        existing.setLngLat([truck.longitude, truck.latitude]);
        const el = existing.getElement();
        const color = truck.isOnline ? riskColors[truck.riskLevel] : "#71717a";
        el.style.background = color;
        el.style.borderColor = truck.isOnline ? "#fff" : "#a1a1aa";
      } else {
        const el = createMarkerEl(truck, () => onTruckClick?.(truckId));
        const marker = new maplibregl.Marker({ element: el })
          .setLngLat([truck.longitude, truck.latitude])
          .addTo(map);
        cur.set(truckId, marker);
      }
    });

    for (const [id, marker] of cur) {
      if (!seen.has(id)) {
        marker.remove();
        cur.delete(id);
      }
    }
  }, [truckStates, mapReady, onTruckClick]);

  // Fly to selected truck
  useEffect(() => {
    if (!selectedTruckId || !mapRef.current) return;
    const truck = truckStates.get(selectedTruckId);
    if (truck) {
      mapRef.current.flyTo({
        center: [truck.longitude, truck.latitude],
        zoom: 10,
        duration: 1000,
      });
    }
  }, [selectedTruckId, truckStates]);

  return (
    <div className="relative h-full w-full">
      <div ref={containerRef} className="h-full w-full" />
      {!mapReady && (
        <div className="absolute inset-0 flex items-center justify-center bg-zinc-100 dark:bg-zinc-900">
          <p className="text-sm text-muted-foreground">Loading map…</p>
        </div>
      )}
      <div className="absolute bottom-3 left-3 flex items-center gap-3 rounded-md bg-white/90 px-3 py-2 text-xs shadow-sm backdrop-blur dark:bg-zinc-900/90">
        <span className="font-medium">{truckStates.size} trucks</span>
        {(["Low", "Medium", "High", "Critical"] as RiskLevel[]).map((r) => (
          <span key={r} className="flex items-center gap-1">
            <span
              className="inline-block h-2.5 w-2.5 rounded-full"
              style={{ backgroundColor: riskColors[r] }}
            />
            {r}
          </span>
        ))}
        <span className="flex items-center gap-1">
          <span className="inline-block h-2.5 w-2.5 rounded-full bg-zinc-500" />
          Offline
        </span>
      </div>
    </div>
  );
}
