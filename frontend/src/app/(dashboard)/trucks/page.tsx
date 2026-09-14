"use client";

import { TruckList } from "@/components/truck-list";

export default function TrucksPage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight">Trucks</h1>
        <p className="text-muted-foreground">
          Browse all trucks with cursor-based pagination and status filters.
        </p>
      </div>

      <TruckList />
    </div>
  );
}
