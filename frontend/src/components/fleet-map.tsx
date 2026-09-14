"use client";

/**
 * Fleet map.
 *
 * Trucks are rendered from a single GeoJSON source with MapLibre's built-in
 * clustering rather than one DOM `Marker` per truck. DOM markers cost a live
 * element each (layout, hit-testing, focus nodes), so a 500-truck fleet made
 * every 2 s state update reconcile hundreds of offscreen nodes. With
 * `cluster: true` the source aggregates points into a handful of visible
 * features, and `setData` replaces the whole fleet in one call.
 *
 * Accessibility note: canvas layers are not keyboard-reachable by design; the
 * trucks table and `/trucks/[id]` route are the accessible path to the same
 * data, and the map is labelled as a complementary view.
 */

import { useEffect, useMemo, useRef, useState } from "react";
import * as maplibregl from "maplibre-gl";
import type {
  GeoJSONSource,
  Map as MapLibreMap,
  MapLayerMouseEvent,
} from "maplibre-gl";
import "maplibre-gl/dist/maplibre-gl.css";
import { useTruckStates } from "@/lib/truck-state-store";
import type { TruckState, RiskLevel } from "@/lib/types";

const riskColors: Record<RiskLevel, string> = {
  Low: "#22c55e",
  Medium: "#f59e0b",
  High: "#f97316",
  Critical: "#ef4444",
};

const OFFLINE_COLOR = "#71717a";

const TRUCKS_SOURCE = "fleet-trucks";
const CLUSTER_LAYER = "fleet-trucks-clusters";
const CLUSTER_COUNT_LAYER = "fleet-trucks-cluster-count";
const TRUCK_LAYER = "fleet-trucks-points";
const SELECTED_LAYER = "fleet-trucks-selected";

/** Radius (px) within which points collapse into one cluster bubble. */
const CLUSTER_RADIUS = 48;

function truckCollection(
  states: Map<string, TruckState>,
): GeoJSON.FeatureCollection {
  const features: GeoJSON.Feature[] = [];
  states.forEach((truck, truckId) => {
    if (!Number.isFinite(truck.latitude) || !Number.isFinite(truck.longitude)) {
      return;
    }
    features.push({
      type: "Feature",
      geometry: {
        type: "Point",
        coordinates: [truck.longitude, truck.latitude],
      },
      properties: {
        truckId,
        riskLevel: truck.riskLevel,
        isOnline: truck.isOnline,
      },
    });
  });
  return { type: "FeatureCollection", features };
}

/** Data-driven fill colour: offline is always grey, otherwise by risk. */
const colorExpression = [
  "case",
  ["==", ["get", "isOnline"], false],
  OFFLINE_COLOR,
  [
    "match",
    ["get", "riskLevel"],
    "Medium",
    riskColors.Medium,
    "High",
    riskColors.High,
    "Critical",
    riskColors.Critical,
    riskColors.Low,
  ],
] as unknown as maplibregl.ExpressionSpecification;

/**
 * Create the clustered fleet source and its layers, plus click/hover handling.
 * Runs once per map instance, after the base style has loaded. `onTruckClick` is
 * read through a ref so the once-bound listener never goes stale.
 */
function addFleetLayers(
  map: MapLibreMap,
  onTruckClick: { current: ((truckId: string) => void) | undefined },
): void {
  map.addSource(TRUCKS_SOURCE, {
    type: "geojson",
    data: { type: "FeatureCollection", features: [] },
    cluster: true,
    clusterRadius: CLUSTER_RADIUS,
    // Beyond this zoom every truck is drawn individually.
    clusterMaxZoom: 12,
  });

  map.addLayer({
    id: CLUSTER_LAYER,
    type: "circle",
    source: TRUCKS_SOURCE,
    filter: ["has", "point_count"],
    paint: {
      "circle-color": "#3b82f6",
      "circle-opacity": 0.85,
      "circle-radius": [
        "interpolate",
        ["linear"],
        ["get", "point_count"],
        2, 12,
        20, 18,
        100, 26,
      ],
      "circle-stroke-color": "#ffffff",
      "circle-stroke-width": 2,
    },
  });

  map.addLayer({
    id: CLUSTER_COUNT_LAYER,
    type: "symbol",
    source: TRUCKS_SOURCE,
    filter: ["has", "point_count"],
    layout: {
      "text-field": ["get", "point_count_abbreviated"],
      "text-size": 11,
    },
    paint: { "text-color": "#ffffff" },
  });

  map.addLayer({
    id: TRUCK_LAYER,
    type: "circle",
    source: TRUCKS_SOURCE,
    filter: ["!", ["has", "point_count"]],
    paint: {
      "circle-color": colorExpression,
      "circle-radius": 6,
      "circle-stroke-color": "#ffffff",
      "circle-stroke-width": 2,
    },
  });

  map.addLayer({
    id: SELECTED_LAYER,
    type: "circle",
    source: TRUCKS_SOURCE,
    filter: ["all", ["!", ["has", "point_count"]], ["==", ["get", "truckId"], "__none__"]],
    paint: {
      "circle-color": "rgba(0,0,0,0)",
      "circle-stroke-color": "#2563eb",
      "circle-stroke-width": 3,
      "circle-radius": 11,
    },
  });

  map.on("click", TRUCK_LAYER, (e: MapLayerMouseEvent) => {
    const id = e.features?.[0]?.properties?.truckId;
    if (typeof id === "string") onTruckClick.current?.(id);
  });

  // Clicking a cluster zooms just enough to expand that bubble.
  map.on("click", CLUSTER_LAYER, async (e: MapLayerMouseEvent) => {
    const feature = e.features?.[0];
    const source = map.getSource(TRUCKS_SOURCE) as GeoJSONSource | undefined;
    const clusterId = feature?.properties?.cluster_id;
    const coords = feature?.geometry as GeoJSON.Point | undefined;
    if (!source || typeof clusterId !== "number" || !coords) return;
    const [lng, lat] = coords.coordinates;
    if (typeof lng !== "number" || typeof lat !== "number") return;
    try {
      const zoom = await source.getClusterExpansionZoom(clusterId);
      map.easeTo({ center: [lng, lat], zoom: Math.max(zoom, 6) });
    } catch {
      // Cluster dissolved between render and click; the next update re-syncs.
    }
  });

  const setPointer = (on: boolean) => {
    map.getCanvas().style.cursor = on ? "pointer" : "";
  };
  map.on("mouseenter", TRUCK_LAYER, () => setPointer(true));
  map.on("mouseleave", TRUCK_LAYER, () => setPointer(false));
  map.on("mouseenter", CLUSTER_LAYER, () => setPointer(true));
  map.on("mouseleave", CLUSTER_LAYER, () => setPointer(false));
}

interface FleetMapProps {
  onTruckClick?: (truckId: string) => void;
  selectedTruckId?: string | null;
}

export function FleetMap({ onTruckClick, selectedTruckId }: FleetMapProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<MapLibreMap | null>(null);
  const clickRef = useRef(onTruckClick);
  const truckStates = useTruckStates();
  const [mapReady, setMapReady] = useState(false);

  // Keep the caller's callback fresh without re-binding map listeners or
  // re-running the source-sync effect on every parent render.
  useEffect(() => {
    clickRef.current = onTruckClick;
  }, [onTruckClick]);

  // Initialize map + fleet layers once
  useEffect(() => {
    if (!containerRef.current || mapRef.current) return;
    const map = new maplibregl.Map({
      container: containerRef.current,
      style: "https://demotiles.maplibre.org/style.json",
      center: [10, 50],
      zoom: 4,
    });
    map.addControl(new maplibregl.NavigationControl(), "top-right");
    map.on("load", () => {
      addFleetLayers(map, clickRef);
      setMapReady(true);
    });
    mapRef.current = map;
    return () => {
      map.remove();
      mapRef.current = null;
      setMapReady(false);
    };
  }, []);

  // Replace the fleet in one immutable setData call. MapLibre re-clusters
  // internally, so cost stays flat as the fleet grows.
  useEffect(() => {
    if (!mapReady || !mapRef.current) return;
    const source = mapRef.current.getSource(TRUCKS_SOURCE) as
      | GeoJSONSource
      | undefined;
    source?.setData(truckCollection(truckStates) as never);
  }, [truckStates, mapReady]);

  // Ring-highlight the selected truck (canvas features, so no DOM node to style).
  useEffect(() => {
    if (!mapReady || !mapRef.current) return;
    mapRef.current.setFilter(SELECTED_LAYER, [
      "all",
      ["!", ["has", "point_count"]],
      ["==", ["get", "truckId"], selectedTruckId ?? "__none__"],
    ]);
  }, [selectedTruckId, mapReady]);

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

  // Legend counts. A live Map of up to 500+ entries is walked once per state
  // change rather than on every render.
  const counts = useMemo(() => {
    const byRisk: Record<RiskLevel, number> = {
      Low: 0,
      Medium: 0,
      High: 0,
      Critical: 0,
    };
    let online = 0;
    truckStates.forEach((t) => {
      if (!t.isOnline) return;
      online += 1;
      byRisk[t.riskLevel] += 1;
    });
    return { online, byRisk, offline: truckStates.size - online };
  }, [truckStates]);

  return (
    <div className="relative h-full w-full">
      <div
        ref={containerRef}
        className="h-full w-full"
        role="img"
        aria-label={`Fleet map: ${truckStates.size} trucks, ${counts.online} online, ${counts.offline} offline. Use the trucks table for a keyboard-navigable list.`}
      />
      {!mapReady && (
        <div className="absolute inset-0 flex items-center justify-center bg-zinc-100 dark:bg-zinc-900">
          <p className="text-sm text-muted-foreground">Loading map…</p>
        </div>
      )}
      <div className="absolute bottom-3 left-3 flex items-center gap-3 rounded-md bg-white/90 px-3 py-2 text-xs shadow-sm backdrop-blur dark:bg-zinc-900/90">
        <span className="font-medium">
          {truckStates.size} trucks
          {counts.offline > 0 && (
            <span className="text-muted-foreground">
              {" · "}
              {counts.online} online
            </span>
          )}
        </span>
        {(["Low", "Medium", "High", "Critical"] as RiskLevel[]).map((r) => (
          <span key={r} className="flex items-center gap-1">
            <span
              className="inline-block h-2.5 w-2.5 rounded-full"
              style={{ backgroundColor: riskColors[r] }}
            />
            {r}
            <span className="tabular-nums text-muted-foreground">
              {counts.byRisk[r]}
            </span>
          </span>
        ))}
        <span className="flex items-center gap-1">
          <span
            className="inline-block h-2.5 w-2.5 rounded-full"
            style={{ backgroundColor: OFFLINE_COLOR }}
          />
          Offline
          <span className="tabular-nums text-muted-foreground">
            {counts.offline}
          </span>
        </span>
      </div>
    </div>
  );
}
