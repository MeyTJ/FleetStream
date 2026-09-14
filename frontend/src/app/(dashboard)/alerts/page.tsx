"use client";

import { ReconnectBanner } from "@/components/reconnect-banner";
import { AlertsPurgeBanner } from "@/components/alerts-purge-banner";
import { AlertFeed } from "@/components/alert-feed";
import { AlertToasts } from "@/components/alert-toast";

export default function AlertsPage() {
  // The hub connection and event subscriptions are owned by the dashboard
  // layout (src/app/(dashboard)/layout.tsx) so they survive navigation.
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
