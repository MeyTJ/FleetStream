"use client";

import Link from "next/link";
import { ChevronLeft, ChevronRight, Truck } from "lucide-react";
import type { Truck as TruckType, TruckStatus } from "@/lib/types";
import { cn } from "@/lib/utils";

// ─── Status badge ─────────────────────────────────────────────────

const statusStyles: Record<TruckStatus, string> = {
  Active: "bg-green-50 text-green-700 dark:bg-green-950 dark:text-green-300",
  Maintenance:
    "bg-yellow-50 text-yellow-700 dark:bg-yellow-950 dark:text-yellow-300",
  Retired: "bg-zinc-100 text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400",
};

export function StatusBadge({ status }: { status: TruckStatus }) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium",
        statusStyles[status],
      )}
    >
      {status}
    </span>
  );
}

// ─── Table content ────────────────────────────────────────────────

interface TruckTableContentProps {
  items: TruckType[];
  totalCount: number;
  pageIndex: number;
  hasMore: boolean;
  search: string;
  onNext: () => void;
  onPrev: () => void;
}

export function TruckTableContent({
  items,
  totalCount,
  pageIndex,
  hasMore,
  search,
  onNext,
  onPrev,
}: TruckTableContentProps) {
  return (
    <>
      <div className="overflow-x-auto rounded-lg border">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b bg-zinc-50 text-left dark:bg-zinc-900">
              <th className="px-4 py-3 font-medium text-muted-foreground">
                ID
              </th>
              <th className="px-4 py-3 font-medium text-muted-foreground">
                Name
              </th>
              <th className="px-4 py-3 font-medium text-muted-foreground">
                Plate
              </th>
              <th className="px-4 py-3 font-medium text-muted-foreground">
                Status
              </th>
              <th className="px-4 py-3 font-medium text-muted-foreground">
                Updated
              </th>
            </tr>
          </thead>
          <tbody>
            {items.map((truck) => (
              <tr
                key={truck.id}
                className="border-b transition-colors hover:bg-zinc-50 dark:hover:bg-zinc-900/50"
              >
                <td className="px-4 py-3">
                  <Link
                    href={`/trucks/${truck.id}`}
                    className="font-mono text-xs text-blue-600 hover:underline dark:text-blue-400"
                  >
                    {truck.id}
                  </Link>
                </td>
                <td className="px-4 py-3 font-medium">{truck.name}</td>
                <td className="px-4 py-3 font-mono text-xs">
                  {truck.licensePlate}
                </td>
                <td className="px-4 py-3">
                  <StatusBadge status={truck.status} />
                </td>
                <td className="px-4 py-3 text-muted-foreground">
                  {new Date(truck.updatedAt).toLocaleString()}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Pagination controls */}
      <div className="flex items-center justify-between">
        <p className="text-sm text-muted-foreground">
          Page {pageIndex + 1} · {items.length} item
          {items.length !== 1 ? "s" : ""}
          {search ? ` (filtered from ${totalCount})` : ""}
        </p>
        <div className="flex gap-2">
          <button
            onClick={onPrev}
            disabled={pageIndex === 0}
            className="flex items-center gap-1 rounded-md border px-3 py-1.5 text-sm transition-colors hover:bg-zinc-50 disabled:cursor-not-allowed disabled:opacity-50 dark:hover:bg-zinc-800"
          >
            <ChevronLeft className="h-4 w-4" />
            Prev
          </button>
          <button
            onClick={onNext}
            disabled={!hasMore}
            className="flex items-center gap-1 rounded-md border px-3 py-1.5 text-sm transition-colors hover:bg-zinc-50 disabled:cursor-not-allowed disabled:opacity-50 dark:hover:bg-zinc-800"
          >
            Next
            <ChevronRight className="h-4 w-4" />
          </button>
        </div>
      </div>
    </>
  );
}

// ─── Empty state ──────────────────────────────────────────────────

export function TruckEmptyState({
  hasFilters,
}: {
  hasFilters: boolean;
}) {
  return (
    <div className="rounded-lg border border-dashed p-12 text-center">
      <Truck className="mx-auto h-10 w-10 text-muted-foreground/50" />
      <h3 className="mt-4 text-lg font-semibold">No trucks found</h3>
      <p className="mt-1 text-sm text-muted-foreground">
        {hasFilters
          ? "Try adjusting your filters."
          : "No truck data available from the BFF."}
      </p>
    </div>
  );
}
