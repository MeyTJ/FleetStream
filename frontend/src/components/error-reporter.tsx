"use client";

/**
 * Installs the global client-side error handlers once, at the very root of the
 * tree, so a crash anywhere below (including in providers) is captured.
 *
 * Lives in its own component because the root layout is a Server Component and
 * cannot call `window.addEventListener` itself.
 */

import { useEffect } from "react";
import {
  composeSinks,
  consoleSink,
  createHttpSink,
  errorReportUrl,
  installErrorReporting,
} from "@/lib/error-reporter";

export function ErrorReporter() {
  useEffect(() => {
    // Local logging is always on: a shipping failure must not also cost the
    // developer the console output. An endpoint is only usable if it also made
    // it into the CSP connect-src (see src/lib/csp.ts); both read the same
    // NEXT_PUBLIC_ERROR_REPORT_URL, so they cannot drift apart.
    const sink = errorReportUrl
      ? composeSinks(consoleSink, createHttpSink({ url: errorReportUrl }))
      : consoleSink;

    return installErrorReporting(sink);
  }, []);

  return null;
}
