"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";

export function QueryProvider({ children }: { children: ReactNode }) {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 5_000, // Match BFF 5 s cache TTL
            refetchInterval: 10_000, // REST polling every 10 s (pre-SignalR)
            // Transport-level retries (5xx + network errors, exponential backoff
            // with full jitter) are handled inside api-client — retrying here
            // would compound attempts (3 query × 3 HTTP = 9 requests).
            // The 10 s polling above provides ongoing resilience instead.
            retry: false,
            refetchOnWindowFocus: true,
          },
        },
      }),
  );

  return (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
}
