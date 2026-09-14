"use client";

import { type ReactNode } from "react";
import { AuthGuard } from "@/components/auth-guard";
import { Header } from "@/components/header";
import { Sidebar } from "@/components/sidebar";
import { SkipToContent } from "@/components/skip-to-content";
import { SignalRProvider } from "@/lib/signalr-provider";
import { useSignalREvents } from "@/lib/hooks/signalr-events";

export default function DashboardLayout({
  children,
}: {
  children: ReactNode;
}) {
  return (
    <AuthGuard>
      {/* One hub connection for the whole dashboard. Mounting per page instead
          meant every navigation dropped the WebSocket, re-ran negotiate, and
          re-joined groups; live state now survives between routes. */}
      <SignalRProvider>
        <SignalREventsMount />
        <div className="flex h-screen flex-col overflow-hidden">
          <SkipToContent />
          <Header />
          <div className="flex flex-1 overflow-hidden">
            <Sidebar />
            <main
              id="main-content"
              tabIndex={-1}
              className="flex-1 overflow-y-auto bg-zinc-50 p-6 outline-none dark:bg-zinc-950"
            >
              {children}
            </main>
          </div>
        </div>
      </SignalRProvider>
    </AuthGuard>
  );
}

/** Subscribes the hub pushes into the client stores; renders nothing. */
function SignalREventsMount() {
  useSignalREvents();
  return null;
}

