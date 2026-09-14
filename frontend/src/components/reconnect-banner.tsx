"use client";

import { WifiOff, Loader2 } from "lucide-react";
import { useSignalR, type SignalRStatus } from "@/lib/signalr-provider";
import { cn } from "@/lib/utils";

const statusConfig: Record<
  SignalRStatus,
  { visible: boolean; text: string; bg: string }
> = {
  connected: { visible: false, text: "", bg: "" },
  connecting: {
    visible: true,
    text: "Connecting to live feed…",
    bg: "bg-blue-50 text-blue-700 dark:bg-blue-950 dark:text-blue-300",
  },
  reconnecting: {
    visible: true,
    text: "Reconnecting…",
    bg: "bg-yellow-50 text-yellow-700 dark:bg-yellow-950 dark:text-yellow-300",
  },
  disconnected: {
    visible: true,
    text: "Live feed disconnected",
    bg: "bg-red-50 text-red-700 dark:bg-red-950 dark:text-red-300",
  },
};

export function ReconnectBanner() {
  const { status } = useSignalR();
  const config = statusConfig[status];

  if (!config.visible) return null;

  return (
    <div
      className={cn(
        "flex items-center gap-2 px-4 py-2 text-sm font-medium",
        config.bg,
      )}
      role="status"
      aria-live="polite"
    >
      {status === "reconnecting" || status === "connecting" ? (
        <Loader2 className="h-4 w-4 animate-spin" />
      ) : (
        <WifiOff className="h-4 w-4" />
      )}
      {config.text}
    </div>
  );
}
