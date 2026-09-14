"use client";

/**
 * System message banner — surfaces OnSystemMessage (SignalR protocol §3.3),
 * the server's ops channel: planned maintenance, backpressure warnings and bulk
 * presence loss.
 *
 * Mounted once in the dashboard layout because the BFF broadcasts this event to
 * all clients (Clients.All), so it is relevant on every route. Severity drives
 * both colour and the ARIA live-politeness: an error must interrupt, an info
 * notice must not.
 */

import { useCallback } from "react";
import { Info, TriangleAlert, OctagonAlert, X } from "lucide-react";
import { useSystemMessage, clearSystemMessage } from "@/lib/system-message-store";
import { cn } from "@/lib/utils";

const severityConfig = {
  info: {
    Icon: Info,
    classes:
      "border-blue-200 bg-blue-50 text-blue-900 dark:border-blue-800 dark:bg-blue-950 dark:text-blue-200",
  },
  warn: {
    Icon: TriangleAlert,
    classes:
      "border-yellow-200 bg-yellow-50 text-yellow-900 dark:border-yellow-800 dark:bg-yellow-950 dark:text-yellow-200",
  },
  error: {
    Icon: OctagonAlert,
    classes:
      "border-red-200 bg-red-50 text-red-900 dark:border-red-800 dark:bg-red-950 dark:text-red-200",
  },
} as const;

export function SystemMessageBanner() {
  const notice = useSystemMessage();
  const dismiss = useCallback(() => clearSystemMessage(), []);

  if (!notice) return null;

  const { Icon, classes } = severityConfig[notice.severity];

  return (
    <div
      role={notice.severity === "info" ? "status" : "alert"}
      aria-live={notice.severity === "info" ? "polite" : "assertive"}
      data-testid="system-message-banner"
      data-severity={notice.severity}
      className={cn(
        "flex items-start gap-3 border-b px-4 py-2 text-sm",
        classes,
      )}
    >
      <Icon className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
      <p className="flex-1">
        <span className="font-medium">{notice.code}</span>
        <span aria-hidden> — </span>
        {notice.message}
      </p>
      <button
        onClick={dismiss}
        aria-label="Dismiss system message"
        className="rounded p-0.5 opacity-60 transition-opacity hover:opacity-100 focus-visible:opacity-100 focus-visible:outline-2 focus-visible:outline-current"
      >
        <X className="h-3.5 w-3.5" />
      </button>
    </div>
  );
}
