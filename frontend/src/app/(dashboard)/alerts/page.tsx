"use client";

import { SignalRProvider } from "@/lib/signalr-provider";
import { useSignalREvents } from "@/lib/hooks/signalr-events";
import { ReconnectBanner } from "@/components/reconnect-banner";
import { AlertsPurgeBanner } from "@/components/alerts-purge-banner";
import { AlertFeed } from "@/components/alert-feed";
import { AlertToasts } from "@/components/alert-toast";

export default function AlertsPage() {
  return (
    <SignalRProvider>
      <AlertsPageInner />
    </SignalRProvider>
  );
}

function AlertsPageInner() {
  useSignalREvents();

  return (
    <div className="space-y-6">
      <ReconnectBanner />
      <AlertsPurgeBanner />
      <div>
        <h1 className="text-2xl font-bold tracking-tight">Alerts</h1>
        <p className="text-muted-foreground">
          Real-time alert feed with severity filtering and acknowledgment.
        </p>
      </div>

      <AlertFeed />
      <AlertToasts />
    </div>
  );
}
