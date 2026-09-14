"use client";

import { AlertTriangle, RefreshCw, Copy } from "lucide-react";
import { ApiError } from "@/lib/api-client";
import { useCallback } from "react";

interface ErrorStateProps {
  error: Error;
  reset?: () => void;
}

/** Standardized error display for API and network failures. */
export function ErrorState({ error, reset }: ErrorStateProps) {
  const isApiError = error instanceof ApiError;
  const title = isApiError ? error.problem.title : "Something went wrong";
  const detail = isApiError
    ? error.problem.detail ?? `HTTP ${error.status}`
    : error.message || "An unexpected error occurred. Please try again.";

  const correlationId = isApiError ? error.correlationId : undefined;
  const traceId = isApiError ? error.traceId : undefined;

  const copyDebugInfo = useCallback(() => {
    const info = [
      correlationId ? `Correlation ID: ${correlationId}` : null,
      traceId ? `Trace ID: ${traceId}` : null,
      `Error: ${title}`,
      detail ? `Detail: ${detail}` : null,
    ]
      .filter(Boolean)
      .join("\n");
    void navigator.clipboard.writeText(info);
  }, [correlationId, traceId, title, detail]);

  return (
    <div className="rounded-lg border border-red-200 bg-red-50 p-6 dark:border-red-900 dark:bg-red-950">
      <div className="flex items-start gap-3">
        <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-red-600 dark:text-red-400" />
        <div className="flex-1 space-y-1">
          <h3 className="text-sm font-semibold text-red-800 dark:text-red-200">
            {title}
          </h3>
          <p className="text-sm text-red-700 dark:text-red-300">{detail}</p>
          {(correlationId || traceId) && (
            <div className="mt-2 flex flex-wrap items-center gap-3">
              {correlationId && (
                <span className="font-mono text-xs text-red-600 dark:text-red-400">
                  Correlation: {correlationId}
                </span>
              )}
              {traceId && (
                <span className="font-mono text-xs text-red-600 dark:text-red-400">
                  Trace: {traceId}
                </span>
              )}
              <button
                onClick={copyDebugInfo}
                className="inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-xs text-red-600 transition-colors hover:bg-red-100 dark:text-red-400 dark:hover:bg-red-900"
                title="Copy debug info"
              >
                <Copy className="h-3 w-3" />
                Copy
              </button>
            </div>
          )}
        </div>
        {reset && (
          <button
            onClick={reset}
            className="flex items-center gap-1.5 rounded-md bg-red-100 px-3 py-1.5 text-sm font-medium text-red-800 transition-colors hover:bg-red-200 dark:bg-red-900 dark:text-red-200 dark:hover:bg-red-800"
          >
            <RefreshCw className="h-3.5 w-3.5" />
            Retry
          </button>
        )}
      </div>
    </div>
  );
}

