"use client";

import { Settings } from "lucide-react";

export default function SettingsPage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight">Settings</h1>
        <p className="text-muted-foreground">
          Application configuration and preferences.
        </p>
      </div>

      <div className="rounded-lg border border-dashed p-12 text-center">
        <Settings className="mx-auto h-10 w-10 text-muted-foreground/50" />
        <h3 className="mt-4 text-lg font-semibold">Settings coming soon</h3>
        <p className="mt-1 text-sm text-muted-foreground">
          Theme preferences, notification settings, and account management.
        </p>
      </div>
    </div>
  );
}
