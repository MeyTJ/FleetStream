"use client";

/**
 * Global error boundary — catches unhandled errors at the root level.
 * This is Next.js's global-error.tsx convention.
 */

import { useEffect } from "react";
import { AlertTriangle, RefreshCw } from "lucide-react";
import { createLogger } from "@/lib/logger";

const log = createLogger("global-error");

export default function GlobalError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    log.error("Global unhandled error", {
      message: error.message,
      digest: error.digest,
    });
  }, [error]);

  return (
    <html lang="en">
      <body className="flex min-h-screen items-center justify-center bg-zinc-50 p-4 dark:bg-zinc-950">
        <div className="flex max-w-md flex-col items-center gap-4 rounded-lg border border-red-200 bg-white p-8 text-center shadow-lg dark:border-red-900 dark:bg-zinc-900">
          <AlertTriangle className="h-10 w-10 text-red-500" />
          <div>
            <h1 className="text-xl font-bold text-red-800 dark:text-red-200">
              Something went wrong
            </h1>
            <p className="mt-2 text-sm text-muted-foreground">
              An unexpected error occurred. Please try refreshing the page.
            </p>
            {error.digest && (
              <p className="mt-1 font-mono text-xs text-muted-foreground">
                Error ID: {error.digest}
              </p>
            )}
          </div>
          <button
            onClick={reset}
            className="flex items-center gap-1.5 rounded-md bg-red-600 px-4 py-2.5 text-sm font-medium text-white transition-colors hover:bg-red-700"
          >
            <RefreshCw className="h-4 w-4" />
            Try again
          </button>
        </div>
      </body>
    </html>
  );
}
