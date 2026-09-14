"use client";

import { useState, useMemo } from "react";
import { Search, Filter } from "lucide-react";
import { useTrucks, type TruckListParams } from "@/lib/hooks/fleet";
import type { TruckStatus } from "@/lib/types";
import { Skeleton } from "@/components/skeleton";
import { ErrorState } from "@/components/error-state";
import {
  TruckTableContent,
  TruckEmptyState,
} from "@/components/truck-table";

function TableSkeleton() {
  return (
    <div className="space-y-3">
      {Array.from({ length: 8 }).map((_, i) => (
        <div
          key={i}
          className="flex items-center gap-4 rounded-md border p-4"
        >
          <Skeleton className="h-4 w-24" />
          <Skeleton className="h-4 w-32" />
          <Skeleton className="h-4 w-20" />
          <Skeleton className="ml-auto h-5 w-16 rounded-full" />
        </div>
      ))}
    </div>
  );
}

export function TruckList() {
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<TruckStatus | "">("");
  const [cursorHistory, setCursorHistory] = useState<(string | undefined)[]>([
    undefined,
  ]);
  const [pageIndex, setPageIndex] = useState(0);

  const currentCursor = cursorHistory[pageIndex];
  const params: TruckListParams = {
    cursor: currentCursor,
    pageSize: 50,
    status: statusFilter || undefined,
  };

  const { data, isLoading, error, refetch } = useTrucks(params);

  // Client-side search filter on current page
  const items = data?.items;
  const filteredItems = useMemo(() => {
    if (!items) return [];
    if (!search.trim()) return items;
    const q = search.toLowerCase();
    return items.filter(
      (t) =>
        t.name.toLowerCase().includes(q) ||
        t.licensePlate.toLowerCase().includes(q) ||
        t.id.toLowerCase().includes(q),
    );
  }, [items, search]);

  function goNext() {
    if (data?.hasMore && data.nextCursor) {
      const next = [...cursorHistory];
      next[pageIndex + 1] = data.nextCursor;
      setCursorHistory(next);
      setPageIndex((p) => p + 1);
    }
  }

  function goPrev() {
    if (pageIndex > 0) setPageIndex((p) => p - 1);
  }

  function handleStatusChange(value: string) {
    setStatusFilter(value as TruckStatus | "");
    setCursorHistory([undefined]);
    setPageIndex(0);
  }

  return (
    <div className="space-y-4">
      {/* Toolbar: search + filter */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="relative max-w-sm flex-1">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <input
            type="text"
            placeholder="Search by name, plate, or ID…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full rounded-md border bg-transparent py-2 pl-9 pr-3 text-sm outline-none transition-colors placeholder:text-muted-foreground focus:ring-2 focus:ring-ring"
          />
        </div>
        <div className="flex items-center gap-2">
          <Filter className="h-4 w-4 text-muted-foreground" />
          <select
            value={statusFilter}
            onChange={(e) => handleStatusChange(e.target.value)}
            className="rounded-md border bg-transparent px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-ring"
          >
            <option value="">All statuses</option>
            <option value="Active">Active</option>
            <option value="Maintenance">Maintenance</option>
            <option value="Retired">Retired</option>
          </select>
        </div>
      </div>

      {/* Content area */}
      {isLoading ? (
        <TableSkeleton />
      ) : error ? (
        <ErrorState error={error} reset={() => refetch()} />
      ) : !data || filteredItems.length === 0 ? (
        <TruckEmptyState hasFilters={!!search || !!statusFilter} />
      ) : (
        <TruckTableContent
          items={filteredItems}
          totalCount={data.items.length}
          pageIndex={pageIndex}
          hasMore={data.hasMore}
          search={search}
          onNext={goNext}
          onPrev={goPrev}
        />
      )}
    </div>
  );
}
