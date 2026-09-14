"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  LayoutDashboard,
  Map,
  AlertTriangle,
  Truck,
  Settings,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { useUnackedAlertCount } from "@/lib/alert-store";

interface NavItem {
  label: string;
  href: string;
  icon: React.ComponentType<{ className?: string }>;
  badge?: number;
}

function useNavItems(): NavItem[] {
  const unackedCount = useUnackedAlertCount();
  return [
    { label: "Dashboard", href: "/", icon: LayoutDashboard },
    { label: "Trucks", href: "/trucks", icon: Truck },
    { label: "Live Map", href: "/map", icon: Map },
    { label: "Alerts", href: "/alerts", icon: AlertTriangle, badge: unackedCount || undefined },
    { label: "Settings", href: "/settings", icon: Settings },
  ];
}

export function Sidebar() {
  const pathname = usePathname();
  const navItems = useNavItems();

  return (
    <aside
      className="hidden w-56 shrink-0 border-r bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-900 md:block"
      aria-label="Main navigation"
    >
      <nav className="flex flex-col gap-1 p-3">
        {navItems.map((item) => {
          const isActive =
            pathname === item.href ||
            (item.href !== "/" && pathname.startsWith(item.href));
          return (
            <Link
              key={item.href}
              href={item.href}
              aria-current={isActive ? "page" : undefined}
              className={cn(
                "flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors",
                isActive
                  ? "bg-blue-50 text-blue-700 dark:bg-blue-950 dark:text-blue-300"
                  : "text-muted-foreground hover:bg-zinc-100 hover:text-foreground dark:hover:bg-zinc-800",
              )}
            >
              <item.icon className="h-4 w-4" />
              <span className="flex-1">{item.label}</span>
              {item.badge !== undefined && item.badge > 0 && (
                <span
                  className="inline-flex h-5 min-w-5 items-center justify-center rounded-full bg-red-500 px-1.5 text-[10px] font-bold text-white"
                  aria-label={`${item.badge} unacknowledged alerts`}
                >
                  {item.badge > 99 ? "99+" : item.badge}
                </span>
              )}
            </Link>
          );
        })}
      </nav>
    </aside>
  );
}
