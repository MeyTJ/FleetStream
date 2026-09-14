"use client";

import { AuthGuard } from "@/components/auth-guard";
import { Header } from "@/components/header";
import { Sidebar } from "@/components/sidebar";
import { SkipToContent } from "@/components/skip-to-content";

export default function DashboardLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <AuthGuard>
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
    </AuthGuard>
  );
}

