"use client";

/**
 * Alerts purge banner — info notice shown when the server broadcasts
 * `OnAlertsPurged` (SignalR protocol §3.3): older ring-buffer entries were
 * evicted server-side and the local feed has been trimmed to match.
 * Auto-dismisses after 10 s and can be closed manually.
 */

import { useCallback, useEffect } from "react";
import { Info, X } from "lucide-react";
import { usePurgeNotice, clearPurgeNotice } from "@/lib/alert-store";

const DISMISS_AFTER_MS = 10_000;

export function AlertsPurgeBanner() {
  const notice = usePurgeNotice();

  const dismiss = useCallback(() => clearPurgeNotice(), []);

  useEffect(() => {
    if (!notice) return;
    const timer = setTimeout(dismiss, DISMISS_AFTER_MS);
    return () => clearTimeout(timer);
  }, [notice, dismiss]);

  if (!notice) return null;

  return (
    <div
      role="status"
      aria-live="polite"
      className="flex items-start gap-3 rounded-lg border border-blue-200 bg-blue-50 px-4 py-3 text-blue-900 dark:border-blue-800 dark:bg-blue-950 dark:text-blue-200"
    >
      <Info className="mt-0.5 h-4 w-4 shrink-0" />
      <p className="flex-1 text-sm">
        {notice.count > 0
          ? `The server purged ${notice.count} older alert${notice.count !== 1 ? "s" : ""}. The local feed was trimmed to match.`
          : "The server purged older alerts; none were present in your local feed."}
      </p>
      <button
        onClick={dismiss}
        aria-label="Dismiss"
        className="rounded p-0.5 opacity-60 transition-opacity hover:opacity-100"
      >
        <X className="h-3.5 w-3.5" />
      </button>
    </div>
  );
}
