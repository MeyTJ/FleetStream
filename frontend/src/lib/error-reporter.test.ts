/**
 * Tests for the global client error handler (src/lib/error-reporter.ts).
 *
 * The dedupe window is module state, so every test resets it first.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  composeSinks,
  createHttpSink,
  installErrorReporting,
  resetErrorReporterForTests,
  scrubCredentials,
  shouldEmit,
  type ErrorReport,
} from "./error-reporter";

const cleanups: Array<() => void> = [];

/** Install with automatic teardown so a failing assertion cannot leak listeners. */
function install(sink: (report: ErrorReport) => void) {
  const cleanup = installErrorReporting(sink);
  cleanups.push(cleanup);
  return cleanup;
}

beforeEach(() => resetErrorReporterForTests());
afterEach(() => {
  // Leftover listeners would consume the shared dedupe budget in later tests,
  // which makes a leak surface as an unrelated count assertion failing.
  while (cleanups.length) cleanups.pop()();
  resetErrorReporterForTests();
});

function dispatchError(message: string) {
  window.dispatchEvent(
    new ErrorEvent("error", {
      message,
      filename: "http://localhost:3000/app.js",
      lineno: 10,
      colno: 2,
    }),
  );
}

function dispatchRejection(reason: unknown) {
  // jsdom requires the `promise` member of PromiseRejectionEventInit; the handler
  // only reads `reason`, so a settled dummy is enough.
  window.dispatchEvent(
    new PromiseRejectionEvent("unhandledrejection", {
      promise: Promise.resolve(),
      reason,
    }),
  );
}

describe("installErrorReporting", () => {
  it("captures an uncaught exception with source location", () => {
    const reports: ErrorReport[] = [];
    const cleanup = install((r) => reports.push(r));

    dispatchError("boom");

    expect(reports).toHaveLength(1);
    expect(reports[0].kind).toBe("uncaught-exception");
    expect(reports[0].message).toBe("boom");
    expect(reports[0].source).toBe("http://localhost:3000/app.js");
    expect(reports[0].line).toBe(10);
    cleanup();
  });

  it("captures an unhandled rejection carrying an Error reason", () => {
    const reports: ErrorReport[] = [];
    const cleanup = install((r) => reports.push(r));

    dispatchRejection(new Error("rejected upstream"));

    expect(reports).toHaveLength(1);
    expect(reports[0].kind).toBe("unhandled-rejection");
    expect(reports[0].message).toBe("rejected upstream");
    cleanup();
  });

  it("handles a non-Error rejection reason instead of throwing", () => {
    const reports: ErrorReport[] = [];
    const cleanup = install((r) => reports.push(r));

    dispatchRejection({ weird: true });

    expect(reports[0].message).toBe("Non-Error promise rejection");
    cleanup();
  });

  it("ignores resource-load error events that carry no message", () => {
    const reports: ErrorReport[] = [];
    const cleanup = install((r) => reports.push(r));

    // A failed <img> fires a bare `error` that bubbles to window.
    window.dispatchEvent(new Event("error"));

    expect(reports).toHaveLength(0);
    cleanup();
  });

  it("stops emitting once the duplicate cap is reached", () => {
    const reports: ErrorReport[] = [];
    const cleanup = install((r) => reports.push(r));

    for (let i = 0; i < 12; i++) dispatchError("same failure");

    expect(reports).toHaveLength(5);
    expect(reports.map((r) => r.occurrence)).toEqual([1, 2, 3, 4, 5]);
    cleanup();
  });

  it("removes its listeners on cleanup", () => {
    const reports: ErrorReport[] = [];
    installErrorReporting((r) => reports.push(r))();

    dispatchError("after teardown");

    expect(reports).toHaveLength(0);
  });

  it("scrubs credentials out of the reported message", () => {
    const reports: ErrorReport[] = [];
    const cleanup = install((r) => reports.push(r));

    dispatchError(
      "GET failed Authorization: Bearer abc.def.ghi token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJvcGVyYXRvci0xIn0.abcdefghijklmnopqrstuvwx",
    );

    const message = reports[0].message;
    expect(message).not.toContain("abc.def.ghi");
    expect(message).not.toContain("eyJhbGciOiJIUzI1NiJ9");
    expect(message).toContain("Bearer [redacted]");
    expect(message).toContain("[redacted-jwt]");
    cleanup();
  });
});

describe("scrubCredentials", () => {
  it("masks bearer and basic schemes", () => {
    expect(scrubCredentials("bearer TOKEN123")).toBe("bearer [redacted]");
    expect(scrubCredentials("Basic TOKEN456==")).toBe("Basic [redacted]");
  });

  it("masks a three-segment JWT embedded in a URL", () => {
    const url =
      "https://api.example.com/hubs?access_token=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.abcdefghij0123456789";
    expect(scrubCredentials(url)).not.toContain("eyJzdWIiOiIxIn0");
  });

  it("leaves ordinary text untouched", () => {
    expect(scrubCredentials("truck TAC-1234 not found")).toBe(
      "truck TAC-1234 not found",
    );
  });
});

describe("shouldEmit", () => {
  it("allows five identical messages and blocks the sixth", () => {
    for (let i = 0; i < 5; i++) expect(shouldEmit("x")).toBe(true);
    expect(shouldEmit("x")).toBe(false);
  });

  it("tracks distinct messages independently", () => {
    expect(shouldEmit("a")).toBe(true);
    expect(shouldEmit("b")).toBe(true);
  });
});

describe("createHttpSink", () => {
  const report = (over?: Partial<ErrorReport>): ErrorReport => ({
    kind: "uncaught-exception",
    message: "boom",
    at: "2026-01-01T00:00:00.000Z",
    occurrence: 1,
    ...over,
  });

  it("POSTs the report as JSON", async () => {
    const fetchImpl = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    const sink = createHttpSink({
      url: "https://errors.example.com/v1/client",
      fetchImpl: fetchImpl as unknown as typeof fetch,
    });

    sink(report());
    await vi.waitFor(() => expect(fetchImpl).toHaveBeenCalledTimes(1));

    const [url, init] = fetchImpl.mock.calls[0];
    expect(url).toBe("https://errors.example.com/v1/client");
    expect(init.method).toBe("POST");
    expect(init.keepalive).toBe(true);
    expect(JSON.parse(init.body).message).toBe("boom");
  });

  it("swallows delivery failures rather than rejecting", async () => {
    const fetchImpl = vi.fn().mockRejectedValue(new Error("network down"));
    const sink = createHttpSink({
      url: "https://errors.example.com/v1/client",
      fetchImpl: fetchImpl as unknown as typeof fetch,
    });

    expect(() => sink(report())).not.toThrow();
    await vi.waitFor(() => expect(fetchImpl).toHaveBeenCalledTimes(1));
  });

  it("drops reports once the pending cap is reached", () => {
    // Never resolves, so every send stays pending and the cap is observable.
    const fetchImpl = vi.fn().mockImplementation(() => new Promise(() => {}));
    const sink = createHttpSink({
      url: "https://errors.example.com/v1/client",
      fetchImpl: fetchImpl as unknown as typeof fetch,
      maxPending: 2,
    });

    sink(report());
    sink(report());
    sink(report());

    expect(fetchImpl).toHaveBeenCalledTimes(2);
  });
});

describe("composeSinks", () => {
  const report: ErrorReport = {
    kind: "uncaught-exception",
    message: "boom",
    at: "2026-01-01T00:00:00.000Z",
    occurrence: 1,
  };

  it("fans a report out to every sink", () => {
    const a = vi.fn();
    const b = vi.fn();

    composeSinks(a, b)(report);

    expect(a).toHaveBeenCalledWith(report);
    expect(b).toHaveBeenCalledWith(report);
  });

  it("keeps delivering to later sinks when an earlier one throws", () => {
    const later = vi.fn();

    expect(() =>
      composeSinks(
        () => {
          throw new Error("sink exploded");
        },
        later,
      )(report),
    ).not.toThrow();
    expect(later).toHaveBeenCalledTimes(1);
  });
});

