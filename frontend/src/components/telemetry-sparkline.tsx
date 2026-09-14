"use client";

/**
 * Telemetry sparkline — plots recent `TruckTelemetry` samples from
 * `GET /api/v1/fleet/trucks/{truckId}/telemetry` (BFF contract §2.2).
 *
 * Two exports:
 *   - `TelemetrySection`    — full section with metric switcher + stats
 *                             (truck detail route)
 *   - `TelemetrySparkline`  — compact single-metric sparkline
 *                             (map detail panel)
 *
 * The BFF returns samples newest-first; the API caps the query window at
 * 24 h (contract §2.4 / validator), so the default window requests 24 h.
 */

import { useMemo, useState } from "react";
import { Activity } from "lucide-react";
import { useTruckTelemetry } from "@/lib/hooks/fleet";
import type { TruckTelemetry } from "@/lib/types";
import { Skeleton } from "@/components/skeleton";
import { ErrorState } from "@/components/error-state";
import { cn } from "@/lib/utils";

// ─── Metric config ────────────────────────────────────────────────

export type TelemetryMetric =
  | "speedKmh"
  | "engineTemperatureCelsius"
  | "fuelLevelPercent"
  | "riskScore";

interface MetricDef {
  label: string;
  unit: string;
  decimals: number;
  /** Sparkline stroke color (hex, used on the SVG path). */
  color: string;
}

const METRICS: Record<TelemetryMetric, MetricDef> = {
  speedKmh: {
    label: "Speed",
    unit: "km/h",
    decimals: 0,
    color: "#2563eb",
  },
  engineTemperatureCelsius: {
    label: "Engine temp",
    unit: "°C",
    decimals: 0,
    color: "#dc2626",
  },
  fuelLevelPercent: {
    label: "Fuel",
    unit: "%",
    decimals: 1,
    color: "#16a34a",
  },
  riskScore: {
    label: "Risk score",
    unit: "",
    decimals: 1,
    color: "#d97706",
  },
};

// ─── Helpers ─────────────────────────────────────────────────────

/** Map keyed samples oldest → newest for chronological plotting. */
type SeriesPoint = Record<TelemetryMetric, number> & {
  eventTimestamp: string;
};

function toSeries(samples: TruckTelemetry[]): SeriesPoint[] {
  return [...samples]
    .sort(
      (a, b) =>
        new Date(a.eventTimestamp).getTime() -
        new Date(b.eventTimestamp).getTime(),
    )
    .map((s) => ({
      eventTimestamp: s.eventTimestamp,
      speedKmh: s.speedKmh,
      engineTemperatureCelsius: s.engineTemperatureCelsius,
      fuelLevelPercent: s.fuelLevelPercent,
      riskScore: s.riskScore,
    }));
}

function fmt(value: number, decimals: number): string {
  return value.toFixed(decimals);
}

function withUnit(value: string, unit: string): string {
  return unit ? `${value} ${unit}` : value;
}

// ─── Sparkline chart (dependency-free SVG) ───────────────────────

function SparklineChart({
  values,
  metricDef,
  height = 112,
  ariaLabel,
}: {
  values: number[];
  metricDef: MetricDef;
  height?: number;
  ariaLabel: string;
}) {
  const width = 640;
  const pad = 2;

  const { linePoints, areaPoints, min, max } = useMemo(() => {
    const min = Math.min(...values);
    const max = Math.max(...values);
    const span = max - min || 1; // Flat series → normalize to middle
    const stepX = values.length > 1 ? (width - pad * 2) / (values.length - 1) : 0;

    const points = values.map((v, i) => {
      const x = pad + i * stepX;
      const y = height - pad - ((v - min) / span) * (height - pad * 2);
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    });

    return {
      linePoints: points.join(" "),
      areaPoints: `${pad},${height - pad} ${points.join(" ")} ${width - pad},${height - pad}`,
      min,
      max,
    };
  }, [values, height]);

  return (
    <figure className="m-0">
      <svg
        viewBox={`0 0 ${width} ${height}`}
        className="h-auto w-full"
        preserveAspectRatio="none"
        role="img"
        aria-label={ariaLabel}
      >
        {values.length === 1 ? (
          <circle
            cx={width / 2}
            cy={height / 2}
            r={3}
            fill={metricDef.color}
          />
        ) : (
          <>
            <polygon
              points={areaPoints}
              fill={metricDef.color}
              opacity={0.12}
            />
            <polyline
              points={linePoints}
              fill="none"
              stroke={metricDef.color}
              strokeWidth={2}
              strokeLinejoin="round"
              strokeLinecap="round"
              vectorEffect="non-scaling-stroke"
            />
          </>
        )}
      </svg>
      <figcaption className="mt-1 flex justify-between text-[10px] text-muted-foreground">
        <span>min {fmt(min, metricDef.decimals)}</span>
        <span>max {fmt(max, metricDef.decimals)}</span>
      </figcaption>
    </figure>
  );
}


// ─── Full section (truck detail page) ───────────────────────────

export function TelemetrySection({ truckId }: { truckId: string }) {
  const [metric, setMetric] = useState<TelemetryMetric>("speedKmh");
  const def = METRICS[metric];
  const { data, isLoading, error, refetch } = useTruckTelemetry(truckId);

  // Oldest → newest for plotting.
  const series = useMemo(() => (data ? toSeries(data) : []), [data]);
  const values = useMemo(() => series.map((s) => s[metric]), [series, metric]);

  const stats = useMemo(() => {
    if (values.length === 0) return null;
    return {
      last: values[values.length - 1],
      min: Math.min(...values),
      max: Math.max(...values),
      avg: values.reduce((sum, v) => sum + v, 0) / values.length,
    };
  }, [values]);

  return (
    <section aria-label="Telemetry history" className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="flex items-center gap-2 text-lg font-semibold">
          <Activity className="h-5 w-5 text-muted-foreground" />
          Telemetry — last 24 h
        </h2>
        <div
          role="group"
          aria-label="Telemetry metric"
          className="flex flex-wrap gap-1"
        >
          {(Object.keys(METRICS) as TelemetryMetric[]).map((key) => (
            <button
              key={key}
              type="button"
              onClick={() => setMetric(key)}
              aria-pressed={metric === key}
              className={cn(
                "rounded-full border px-3 py-1 text-xs font-medium transition-colors",
                metric === key
                  ? "border-blue-300 bg-blue-50 text-blue-700 dark:border-blue-800 dark:bg-blue-950 dark:text-blue-300"
                  : "border-zinc-200 text-muted-foreground hover:bg-zinc-50 dark:border-zinc-700 dark:hover:bg-zinc-800",
              )}
            >
              {METRICS[key].label}
            </button>
          ))}
        </div>
      </div>

      {isLoading ? (
        <div className="space-y-2">
          <Skeleton className="h-28 w-full" />
          <Skeleton className="h-4 w-64" />
        </div>
      ) : error ? (
        <ErrorState error={error} reset={() => refetch()} />
      ) : !stats ? (
        <div className="rounded-lg border border-dashed p-8 text-center">
          <Activity className="mx-auto h-8 w-8 text-muted-foreground/50" />
          <h3 className="mt-3 text-sm font-semibold">No telemetry</h3>
          <p className="mt-1 text-xs text-muted-foreground">
            No telemetry samples recorded for this truck in the last 24 hours.
          </p>
        </div>
      ) : (
        <div className="space-y-3 rounded-lg border bg-white p-4 dark:bg-zinc-900">
          <SparklineChart
            values={values}
            metricDef={def}
            ariaLabel={`${def.label} over the last 24 hours — latest ${withUnit(fmt(stats.last, def.decimals), def.unit)}, ${values.length} samples`}
          />
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-5">
            <Stat
              label={`${def.label} now`}
              value={withUnit(fmt(stats.last, def.decimals), def.unit)}
              accent
            />
            <Stat label="Min" value={withUnit(fmt(stats.min, def.decimals), def.unit)} />
            <Stat label="Max" value={withUnit(fmt(stats.max, def.decimals), def.unit)} />
            <Stat label="Average" value={withUnit(fmt(stats.avg, def.decimals), def.unit)} />
            <Stat label="Samples" value={String(values.length)} />
          </div>
          <p className="text-xs text-muted-foreground">
            {new Date(series[0].eventTimestamp).toLocaleString()} —{" "}
            {new Date(series[series.length - 1].eventTimestamp).toLocaleString()}
          </p>
        </div>
      )}
    </section>
  );
}

function Stat({
  label,
  value,
  accent = false,
}: {
  label: string;
  value: string;
  accent?: boolean;
}) {
  return (
    <div>
      <p className="text-xs text-muted-foreground">{label}</p>
      <p
        className={cn(
          "mt-0.5 text-sm font-semibold tabular-nums",
          accent && "text-blue-600 dark:text-blue-400",
        )}
      >
        {value}
      </p>
    </div>
  );
}

// ─── Compact sparkline (map detail panel) ────────────────────────

export function TelemetrySparkline({
  truckId,
  metric = "speedKmh",
  hours = 24,
  limit = 60,
}: {
  truckId: string;
  metric?: TelemetryMetric;
  hours?: number;
  limit?: number;
}) {
  const def = METRICS[metric];
  const { data, isLoading } = useTruckTelemetry(truckId, { hours, limit });

  const values = useMemo(
    () => (data ? toSeries(data).map((s) => s[metric]) : []),
    [data, metric],
  );

  if (isLoading) return <Skeleton className="h-20 w-full" />;

  return (
    <div className="rounded-lg border bg-zinc-50 p-3 dark:bg-zinc-800">
      <div className="flex items-baseline justify-between gap-2">
        <span className="text-xs font-medium text-muted-foreground">
          {def.label} — last {hours} h
        </span>
        {values.length > 0 && (
          <span
            className="text-sm font-semibold tabular-nums"
            style={{ color: def.color }}
          >
            {withUnit(fmt(values[values.length - 1], def.decimals), def.unit)}
          </span>
        )}
      </div>
      {values.length === 0 ? (
        <p className="mt-2 text-xs text-muted-foreground">
          No telemetry samples.
        </p>
      ) : (
        <div className="mt-2">
          <SparklineChart
            values={values}
            metricDef={def}
            height={64}
            ariaLabel={`${def.label} over the last ${hours} hours, ${values.length} samples`}
          />
        </div>
      )}
    </div>
  );
}
