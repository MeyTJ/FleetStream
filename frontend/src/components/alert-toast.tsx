"use client";

/**
 * Alert toast — shows a toast notification for incoming critical/error alerts.
 * Listens to the alert store and auto-dismisses after 5 seconds.
 */

import { useEffect, useRef, useState, useCallback } from "react";
import { X, AlertTriangle } from "lucide-react";
import { useAlerts } from "@/lib/alert-store";
import type { Alert, AlertSeverity } from "@/lib/types";
import { cn } from "@/lib/utils";

const TOAST_DURATION_MS = 5_000;

const severityStyles: Record<AlertSeverity, string> = {
  Info: "border-blue-200 bg-blue-50 text-blue-900 dark:border-blue-800 dark:bg-blue-950 dark:text-blue-200",
  Warning:
    "border-yellow-200 bg-yellow-50 text-yellow-900 dark:border-yellow-800 dark:bg-yellow-950 dark:text-yellow-200",
  Error:
    "border-orange-200 bg-orange-50 text-orange-900 dark:border-orange-800 dark:bg-orange-950 dark:text-orange-200",
  Critical:
    "border-red-200 bg-red-50 text-red-900 dark:border-red-800 dark:bg-red-950 dark:text-red-200",
};

export function AlertToasts() {
  const alerts = useAlerts();
  const [toasts, setToasts] = useState<Alert[]>([]);
  const seenRef = useRef(new Set<string>());

  // Watch for new critical/error alerts
  useEffect(() => {
    for (const alert of alerts) {
      if (
        !seenRef.current.has(alert.id) &&
        (alert.severity === "Critical" || alert.severity === "Error")
      ) {
        seenRef.current.add(alert.id);
        setToasts((prev) => [alert, ...prev].slice(0, 3)); // Max 3 toasts
      }
    }
  }, [alerts]);

  const dismiss = useCallback((alertId: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== alertId));
  }, []);

  // Auto-dismiss after duration
  useEffect(() => {
    if (toasts.length === 0) return;

    const timers = toasts.map((toast) =>
      setTimeout(() => dismiss(toast.id), TOAST_DURATION_MS),
    );

    return () => {
      for (const timer of timers) clearTimeout(timer);
    };
  }, [toasts, dismiss]);

  if (toasts.length === 0) return null;

  return (
    <div className="fixed bottom-4 right-4 z-50 flex flex-col gap-2">
      {toasts.map((toast) => (
        <div
          key={toast.id}
          className={cn(
            "flex w-80 items-start gap-3 rounded-lg border p-3 shadow-lg",
            severityStyles[toast.severity],
          )}
          role="alert"
        >
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <div className="flex-1 min-w-0">
            <p className="text-xs font-semibold">{toast.alertType}</p>
            <p className="mt-0.5 text-xs opacity-80 truncate">
              {toast.message}
            </p>
          </div>
          <button
            onClick={() => dismiss(toast.id)}
            className="shrink-0 rounded p-0.5 opacity-60 transition-opacity hover:opacity-100"
            aria-label="Dismiss"
          >
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      ))}
    </div>
  );
}
