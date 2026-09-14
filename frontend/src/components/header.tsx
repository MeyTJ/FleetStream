"use client";

import { useAuth } from "@/lib/auth-context";
import { LogOut, Truck } from "lucide-react";
import Link from "next/link";

export function Header() {
  const { subject, logout } = useAuth();

  return (
    <header className="flex h-14 shrink-0 items-center justify-between border-b bg-white px-4 dark:border-zinc-800 dark:bg-zinc-950">
      <Link href="/" className="flex items-center gap-2 font-semibold">
        <Truck className="h-5 w-5 text-blue-600" />
        <span>FleetStream</span>
      </Link>

      <div className="flex items-center gap-3">
        <span className="text-sm text-muted-foreground">{subject}</span>
        <button
          onClick={logout}
          className="flex items-center gap-1.5 rounded-md px-2 py-1.5 text-sm text-muted-foreground transition-colors hover:bg-zinc-100 hover:text-foreground dark:hover:bg-zinc-800"
          aria-label="Sign out"
        >
          <LogOut className="h-4 w-4" />
          <span className="hidden sm:inline">Sign out</span>
        </button>
      </div>
    </header>
  );
}
