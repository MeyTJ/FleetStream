"use client";

/**
 * Alert feed — shows live alerts with severity filtering,
 * client-side ring buffer from SignalR + REST initial load,
 * and optimistic acknowledge action.
 */

import { useState, useMemo, useCallback, useEffect, useRef } from "react";
import {
  AlertTriangle,
  CheckCircle2,
  Filter,
  Bell,
  Info,
  AlertOctagon,
  ShieldAlert,
  Loader2,
  Truck,
  ChevronDown,
  ChevronUp,
} from "lucide-react";
import { useAlerts as useLiveAlerts } from "@/lib/alert-store";
import {
  useAlerts as useRestAlerts,
  useAcknowledgeAlert,
} from "@/lib/hooks/fleet";
import { useAuth } from "@/lib/auth-context";
import { getJwtSubject, hasAnyRole } from "@/lib/jwt";
import { optimisticAck, rollbackAck, replaceAlerts } from "@/lib/alert-store";
import type { Alert, AlertSeverity } from "@/lib/types";
import { cn } from "@/lib/utils";


// ─── Severity config ──────────────────────────────────────────

const severityConfig: Record<
  AlertSeverity,
  {
    icon: React.ComponentType<{ className?: string }>;
    color: string;
    bg: string;
    label: string;
  }
> = {
  Info: {
    icon: Info,
    color: "text-blue-600 dark:text-blue-400",
    bg: "bg-blue-50 dark:bg-blue-950",
    label: "Info",
  },
  Warning: {
    icon: AlertTriangle,
    color: "text-yellow-600 dark:text-yellow-400",
    bg: "bg-yellow-50 dark:bg-yellow-950",
    label: "Warning",
  },
  Error: {
    icon: AlertOctagon,
    color: "text-orange-600 dark:text-orange-400",
    bg: "bg-orange-50 dark:bg-orange-950",
    label: "Error",
  },
  Critical: {
    icon: ShieldAlert,
    color: "text-red-600 dark:text-red-400",
    bg: "bg-red-50 dark:bg-red-950",
    label: "Critical",
  },
};

const ALL_SEVERITIES: AlertSeverity[] = [
  "Critical",
  "Error",
  "Warning",
  "Info",
];


// ─── Helpers ──────────────────────────────────────────────────

function relativeTime(ts: string): string {
  const diff = Date.now() - new Date(ts).getTime();
  if (diff < 60_000) return "just now";
  if (diff < 3_600_000) return `${Math.floor(diff / 60_000)}m ago`;
  if (diff < 86_400_000) return `${Math.floor(diff / 3_600_000)}h ago`;
  return `${Math.floor(diff / 86_400_000)}d ago`;
}


// ─── Sub-components ───────────────────────────────────────────

function Toolbar({
  count,
  showFilters,
  onToggleFilters,
}: {
  count: number;
  showFilters: boolean;
  onToggleFilters: () => void;
}) {
  return (
    <div className="flex items-center justify-between">
      <div className="flex items-center gap-2">
        <Bell className="h-4 w-4 text-muted-foreground" />
        <span className="text-sm text-muted-foreground">
          {count} alert{count !== 1 ? "s" : ""}
        </span>
      </div>
      <button
        onClick={onToggleFilters}
        aria-expanded={showFilters}
        aria-controls="alert-severity-filters"
        className="flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-sm transition-colors hover:bg-zinc-50 dark:hover:bg-zinc-800"
      >
        <Filter className="h-3.5 w-3.5" />
        Filters
        {showFilters ? (
          <ChevronUp className="h-3.5 w-3.5" />
        ) : (
          <ChevronDown className="h-3.5 w-3.5" />
        )}
      </button>
    </div>
  );
}


function FilterBar({
  active,
  onToggle,
  onClear,
}: {
  active: Set<AlertSeverity>;
  onToggle: (s: AlertSeverity) => void;
  onClear: () => void;
}) {
  return (
    <div
      id="alert-severity-filters"
      className="flex flex-wrap gap-2 rounded-lg border p-3"
    >
      {ALL_SEVERITIES.map((severity) => {
        const config = severityConfig[severity];
        const isActive = active.has(severity);
        return (
          <button
            key={severity}
            onClick={() => onToggle(severity)}
            aria-pressed={isActive}
            className={cn(
              "flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-medium transition-colors",
              isActive
                ? cn(config.bg, config.color, "border-current")
                : "border-zinc-200 text-muted-foreground hover:bg-zinc-50 dark:border-zinc-700 dark:hover:bg-zinc-800",
            )}
          >
            <config.icon className="h-3 w-3" />
            {config.label}
          </button>
        );
      })}
      {active.size > 0 && (
        <button
          onClick={onClear}
          className="rounded-full px-3 py-1 text-xs text-muted-foreground hover:underline"
        >
          Clear
        </button>
      )}
    </div>
  );
}

function EmptyState({ hasFilters }: { hasFilters: boolean }) {
  return (
    <div className="rounded-lg border border-dashed p-12 text-center">
      <CheckCircle2 className="mx-auto h-10 w-10 text-muted-foreground/50" />
      <h3 className="mt-4 text-lg font-semibold">No alerts</h3>
      <p className="mt-1 text-sm text-muted-foreground">
        {hasFilters
          ? "No alerts matching the selected filters."
          : "All clear — no active alerts in the fleet."}
      </p>
    </div>
  );
}


// ─── Main component ───────────────────────────────────────────

export function AlertFeed() {
  const liveAlerts = useLiveAlerts();
  const [activeSeverities, setActiveSeverities] = useState<Set<AlertSeverity>>(
    new Set(),
  );
  const [showFilters, setShowFilters] = useState(false);
  const [acknowledging, setAcknowledging] = useState<Set<string>>(new Set());
  const { token } = useAuth();
  const ackMutation = useAcknowledgeAlert();
  const loadedRef = useRef(false);

  // Ack permission mirrors the BFF "AlertsAck" policy:
  // RequireRole("alerts:ack", "fleet:admin") — either role grants ack.
  const canAck = useMemo(
    () => hasAnyRole(token, "alerts:ack", "fleet:admin"),
    [token],
  );

  const { data: restData } = useRestAlerts({
    pageSize: 100,
    onlyActive: true,
  });

  useEffect(() => {
    if (restData?.items && !loadedRef.current) {
      loadedRef.current = true;
      replaceAlerts(restData.items);
    }
  }, [restData]);

  const filtered = useMemo(() => {
    if (activeSeverities.size === 0) return liveAlerts;
    return liveAlerts.filter((a) => activeSeverities.has(a.severity));
  }, [liveAlerts, activeSeverities]);

  const toggleSeverity = useCallback((severity: AlertSeverity) => {
    setActiveSeverities((prev) => {
      const next = new Set(prev);
      if (next.has(severity)) next.delete(severity);
      else next.add(severity);
      return next;
    });
  }, []);

  const handleAck = useCallback(
    async (alert: Alert) => {
      if (!token || acknowledging.has(alert.id)) return;
      const subject = getJwtSubject(token) ?? "unknown";
      const prev = optimisticAck(alert.id, subject);
      setAcknowledging((s) => new Set(s).add(alert.id));
      try {
        await ackMutation.mutateAsync({
          alertId: alert.id,
          acknowledgedBy: subject,
        });
      } catch {
        if (prev) rollbackAck(alert.id, prev);
      } finally {
        setAcknowledging((s) => {
          const next = new Set(s);
          next.delete(alert.id);
          return next;
        });
      }
    },
    [token, acknowledging, ackMutation],
  );

  return (
    <div className="space-y-4">
      <Toolbar
        count={filtered.length}
        showFilters={showFilters}
        onToggleFilters={() => setShowFilters((v) => !v)}
      />
      {showFilters && (
        <FilterBar
          active={activeSeverities}
          onToggle={toggleSeverity}
          onClear={() => setActiveSeverities(new Set())}
        />
      )}
      {filtered.length === 0 ? (
        <EmptyState hasFilters={activeSeverities.size > 0} />
      ) : (
        <div className="space-y-2">
          {filtered.map((alert) => (
            <AlertRow
              key={alert.id}
              alert={alert}
              canAck={canAck}
              isAcking={acknowledging.has(alert.id)}
              onAck={handleAck}
            />
          ))}
        </div>
      )}
    </div>
  );
}


// ─── Alert row ────────────────────────────────────────────────

function AlertRow({
  alert,
  canAck,
  isAcking,
  onAck,
}: {
  alert: Alert;
  canAck: boolean;
  isAcking: boolean;
  onAck: (alert: Alert) => void;
}) {
  const config = severityConfig[alert.severity];
  const Icon = config.icon;

  return (
    <div
      className={cn(
        "flex items-start gap-3 rounded-lg border p-3 transition-colors",
        alert.isAcknowledged
          ? "border-zinc-200 bg-zinc-50/50 opacity-60 dark:border-zinc-800 dark:bg-zinc-900/50"
          : "bg-white dark:bg-zinc-900",
      )}
    >
      <div
        className={cn(
          "mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-full",
          config.bg,
        )}
      >
        <Icon className={cn("h-4 w-4", config.color)} />
      </div>

      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2">
          <span className={cn("text-xs font-semibold", config.color)}>
            {config.label}
          </span>
          <span className="text-xs text-muted-foreground">·</span>
          <span className="text-xs font-medium">{alert.alertType}</span>
          <span className="text-xs text-muted-foreground">·</span>
          <span className="flex items-center gap-1 text-xs text-muted-foreground">
            <Truck className="h-3 w-3" />
            {alert.truckId}
          </span>
        </div>
        <p className="mt-1 text-sm">{alert.message}</p>
        <div className="mt-2 flex items-center gap-3">
          <span className="text-xs text-muted-foreground">
            {relativeTime(alert.timestamp)}
          </span>
          {alert.isAcknowledged && (
            <span className="flex items-center gap-1 text-xs text-green-600 dark:text-green-400">
              <CheckCircle2 className="h-3 w-3" />
              Acked by {alert.acknowledgedBy}
            </span>
          )}
        </div>
      </div>

      {canAck && !alert.isAcknowledged && (
        <button
          onClick={() => onAck(alert)}
          disabled={isAcking}
          className={cn(
            "shrink-0 flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-xs font-medium transition-colors",
            "border-zinc-200 hover:bg-green-50 hover:text-green-700 hover:border-green-200",
            "dark:border-zinc-700 dark:hover:bg-green-950 dark:hover:text-green-300 dark:hover:border-green-800",
            "disabled:opacity-50 disabled:cursor-not-allowed",
          )}
        >
          {isAcking ? (
            <Loader2 className="h-3 w-3 animate-spin" />
          ) : (
            <CheckCircle2 className="h-3 w-3" />
          )}
          Ack
        </button>
      )}
    </div>
  );
}
