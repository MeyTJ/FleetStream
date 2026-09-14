/**
 * Client-side error reporting for FleetStream.
 *
 * Before this module, unhandled exceptions and rejected promises were caught by
 * nothing at all: `logger.ts` only records calls made explicitly by app code, so
 * a crash inside an event handler or a forgotten `await` vanished. This installs
 * the two global handlers (`error` and `unhandledrejection`) and forwards a
 * structured record to a pluggable sink.
 *
 * - The sink is injected: no collector vendor is configured yet, and the default
 *   of `console` keeps this safe to import from tests and the browser bundle.
 * - An HTTP endpoint must be baked in through NEXT_PUBLIC_ERROR_REPORT_URL, since
 *   adding a runtime origin without updating CSP `connect-src` (see csp.ts) would
 *   make the reports fail silently in production.
 */

import { createLogger } from "./logger";

const log = createLogger("error-reporter");

/** A single captured client-side failure. */
export interface ErrorReport {
  /** Where the failure came from. */
  kind: "uncaught-exception" | "unhandled-rejection";
  message: string;
  /** Stack when the source gave one; ErrorEvent only exposes a filename. */
  stack?: string;
  /** Source URL, when known. */
  source?: string;
  line?: number;
  column?: number;
  /** ISO timestamp captured at report time. */
  at: string;
  /** How many times this exact message has been reported, inclusive. */
  occurrence: number;
}

export type ErrorSink = (report: ErrorReport) => void;

// ─── Deduplication ─────────────────────────────────────────────────
//
// React re-renders and SignalR reconnect loops surface the same failure on every
// retry. Uncapped, one defect becomes a console (or network) flood, which is its
// own availability problem.
const MAX_IDENTICAL_REPORTS = 5;
const seen = new Map<string, number>();

/** Test hook: forget the dedupe window. */
export function resetErrorReporterForTests(): void {
  seen.clear();
}

/** Count already recorded for a message; 0 when never seen. */
export function errorOccurrenceCount(message: string): number {
  return seen.get(message) ?? 0;
}

/** True when this message is still under the duplicate cap. Mutates the window. */
export function shouldEmit(message: string): boolean {
  const next = (seen.get(message) ?? 0) + 1;
  seen.set(message, next);
  return next <= MAX_IDENTICAL_REPORTS;
}

// ─── Payload scrubbing ─────────────────────────────────────────────
//
// Tokens routinely end up in fetch error messages and SignalR negotiation URLs.
// Shipping them to a collector is a credential leak, so anything resembling a
// bearer token or JWT is masked before it leaves the browser.
const BEARER_PATTERN = /\b(Bearer|Basic)\s+[A-Za-z0-9._~+/-]+=*/gi;
// Three base64url segments, i.e. a JWT (the header always starts "eyJ").
const JWT_PATTERN = /\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}/g;

/** Replace credential-looking substrings with a stable placeholder. */
export function scrubCredentials(text: string): string {
  return text
    .replace(BEARER_PATTERN, "$1 [redacted]")
    .replace(JWT_PATTERN, "[redacted-jwt]");
}


// ─── Console sink ──────────────────────────────────────────────────

/** Default sink: one structured console line per report. */
export const consoleSink: ErrorSink = (report) => {
  log.error(report.message, {
    kind: report.kind,
    at: report.at,
    occurrence: report.occurrence,
    source: report.source,
    line: report.line,
    column: report.column,
    stack: report.stack,
  });
};

// ─── HTTP sink ─────────────────────────────────────────────────────

/**
 * Combine sinks into one, isolating each from the others' failures.
 *
 * Needed because installing the global handlers twice would consume the
 * deduplication budget once per installation, halving how many reports actually
 * get through.
 */
export function composeSinks(...sinks: ErrorSink[]): ErrorSink {
  return (report) => {
    for (const sink of sinks) {
      try {
        sink(report);
      } catch {
        // A broken sink must never break the others or the app.
      }
    }
  };
}

export interface HttpSinkOptions {
  /** Collector endpoint, e.g. https://errors.example.com/v1/client. */
  url: string;
  /** Override fetch for tests. Defaults to globalThis.fetch. */
  fetchImpl?: typeof fetch;
  /** Cap on concurrent sends so a crash loop cannot saturate the network. */
  maxPending?: number;
}

/**
 * Build a sink that POSTs reports to `url`.
 *
 * Fire-and-forget: a failed send is logged locally and never re-queued, because
 * the reporter must not become a source of unhandled rejections itself. The
 * request is `keepalive` so a report still has a chance to land if the page is
 * going away.
 */
export function createHttpSink(options: HttpSinkOptions): ErrorSink {
  const doFetch = options.fetchImpl ?? globalThis.fetch?.bind(globalThis);
  const maxPending = options.maxPending ?? 10;
  let pending = 0;

  return (report) => {
    if (!doFetch) return;
    if (pending >= maxPending) {
      log.warn("Error report dropped; too many pending", { url: options.url });
      return;
    }
    pending += 1;
    void doFetch(options.url, {
      method: "POST",
      keepalive: true,
      headers: { "content-type": "application/json" },
      body: JSON.stringify(report),
    })
      .catch(() => {
        // Swallowed on purpose: delivery failures must not cascade.
        log.warn("Error report delivery failed", { url: options.url });
      })
      .finally(() => {
        pending -= 1;
      });
  };
}

// ─── Handler installation ──────────────────────────────────────────

/** Removes the listeners attached by {@link installErrorReporting}. */
export type ErrorReporterCleanup = () => void;

/**
 * Attach `error` and `unhandledrejection` window listeners.
 *
 * A no-op during server rendering, where `window` does not exist. Callers that
 * install twice get two independent pairs of listeners and two cleanups.
 */
export function installErrorReporting(
  sink: ErrorSink = consoleSink,
): ErrorReporterCleanup {
  if (typeof window === "undefined") return () => {};

  const emit = (
    kind: ErrorReport["kind"],
    partial: Pick<ErrorReport, "message" | "stack" | "source" | "line" | "column">,
  ) => {
    const message = scrubCredentials(partial.message || `${kind}: unknown`);
    if (!shouldEmit(message)) return;
    sink({
      kind,
      message,
      stack: partial.stack,
      source: partial.source,
      line: partial.line,
      column: partial.column,
      at: new Date().toISOString(),
      occurrence: errorOccurrenceCount(message),
    });
  };

  const onError = (event: ErrorEvent) => {
    // Failed <img>/<script> loads also fire `error` on window but carry no
    // message; those are asset misses, not application faults.
    if (!event.message) return;
    emit("uncaught-exception", {
      message: event.message,
      source: event.filename,
      line: event.lineno,
      column: event.colno,
      stack: event.error instanceof Error ? event.error.stack : undefined,
    });
  };

  const onRejection = (event: PromiseRejectionEvent) => {
    const reason = event.reason;
    const message =
      reason instanceof Error
        ? reason.message
        : typeof reason === "string"
          ? reason
          : "Non-Error promise rejection";
    emit("unhandled-rejection", {
      message,
      stack: reason instanceof Error ? reason.stack : undefined,
    });
  };

  window.addEventListener("error", onError);
  window.addEventListener("unhandledrejection", onRejection);

  return () => {
    window.removeEventListener("error", onError);
    window.removeEventListener("unhandledrejection", onRejection);
  };
}

/**
 * The collector endpoint baked into the bundle, if any. Kept in sync with the
 * CSP: configuring this without adding the origin to connect-src means reports
 * are blocked by the browser.
 */
export const errorReportUrl: string | undefined =
  process.env.NEXT_PUBLIC_ERROR_REPORT_URL || undefined;

