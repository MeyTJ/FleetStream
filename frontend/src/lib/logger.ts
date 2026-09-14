/**
 * Structured client-side logger for FleetStream.
 *
 * - Level-based (debug, info, warn, error)
 * - Includes timestamps and optional context
 * - Production-safe: debug/info silenced when NEXT_PUBLIC_LOG_LEVEL is "warn" or "error"
 * - Correlation IDs from API errors attached to logs
 */

type LogLevel = "debug" | "info" | "warn" | "error";

const LEVEL_ORDER: Record<LogLevel, number> = {
  debug: 0,
  info: 1,
  warn: 2,
  error: 3,
};

const configuredLevel: LogLevel =
  (process.env.NEXT_PUBLIC_LOG_LEVEL as LogLevel) ?? "info";

function shouldLog(level: LogLevel): boolean {
  return LEVEL_ORDER[level] >= LEVEL_ORDER[configuredLevel];
}

function formatPrefix(level: LogLevel, context?: string): string {
  const ts = new Date().toISOString();
  const tag = context ? `[${context}]` : "";
  return `${ts} ${level.toUpperCase()}${tag}`;
}

interface LogMeta {
  [key: string]: unknown;
}

function log(
  level: LogLevel,
  message: string,
  meta?: LogMeta,
  context?: string,
): void {
  if (!shouldLog(level)) return;

  const prefix = formatPrefix(level, context);
  const args = meta ? [prefix, message, meta] : [prefix, message];

  switch (level) {
    case "debug":
      console.debug(...args);
      break;
    case "info":
      console.info(...args);
      break;
    case "warn":
      console.warn(...args);
      break;
    case "error":
      console.error(...args);
      break;
  }
}

/** Create a scoped logger with a fixed context label. */
export function createLogger(context: string) {
  return {
    debug: (message: string, meta?: LogMeta) =>
      log("debug", message, meta, context),
    info: (message: string, meta?: LogMeta) =>
      log("info", message, meta, context),
    warn: (message: string, meta?: LogMeta) =>
      log("warn", message, meta, context),
    error: (message: string, meta?: LogMeta) =>
      log("error", message, meta, context),
  };
}

/** Default unscoped logger. */
export const logger = {
  debug: (message: string, meta?: LogMeta) => log("debug", message, meta),
  info: (message: string, meta?: LogMeta) => log("info", message, meta),
  warn: (message: string, meta?: LogMeta) => log("warn", message, meta),
  error: (message: string, meta?: LogMeta) => log("error", message, meta),
};
