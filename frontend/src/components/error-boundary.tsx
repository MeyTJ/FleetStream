"use client";

/**
 * Reusable React error boundary for catching rendering errors.
 *
 * Wraps children and displays a fallback UI when an unhandled error occurs.
 * Provides a "Try again" button that resets the error state.
 */

import { Component, type ErrorInfo, type ReactNode } from "react";
import { AlertTriangle, RefreshCw } from "lucide-react";
import { createLogger } from "@/lib/logger";

const log = createLogger("ErrorBoundary");

interface Props {
  children: ReactNode;
  /** Custom fallback renderer. Receives the error and a reset function. */
  fallback?: (error: Error, reset: () => void) => ReactNode;
  /** Optional label for logging context. */
  label?: string;
}

interface State {
  error: Error | null;
}

export class ErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props);
    this.state = { error: null };
  }

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    log.error("Unhandled rendering error", {
      label: this.props.label ?? "ErrorBoundary",
      message: error.message,
      componentStack: info.componentStack,
    });
  }

  reset = (): void => {
    this.setState({ error: null });
  };

  render() {
    const { error } = this.state;
    if (!error) return this.props.children;

    if (this.props.fallback) {
      return this.props.fallback(error, this.reset);
    }

    return (
      <div
        className="flex flex-col items-center justify-center gap-4 rounded-lg border border-red-200 bg-red-50 p-8 text-center dark:border-red-900 dark:bg-red-950"
        role="alert"
      >
        <AlertTriangle className="h-8 w-8 text-red-500" />
        <div>
          <h2 className="text-lg font-semibold text-red-800 dark:text-red-200">
            Something went wrong
          </h2>
          <p className="mt-1 text-sm text-red-700 dark:text-red-300">
            {error.message || "An unexpected error occurred."}
          </p>
        </div>
        <button
          onClick={this.reset}
          className="flex items-center gap-1.5 rounded-md bg-red-100 px-4 py-2 text-sm font-medium text-red-800 transition-colors hover:bg-red-200 dark:bg-red-900 dark:text-red-200 dark:hover:bg-red-800"
        >
          <RefreshCw className="h-3.5 w-3.5" />
          Try again
        </button>
      </div>
    );
  }
}
