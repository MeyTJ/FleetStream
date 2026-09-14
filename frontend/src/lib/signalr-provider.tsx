"use client";

/**
 * SignalR hub connection provider for FleetStream.
 *
 * - Singleton hub connection per app lifecycle
 * - Exponential backoff reconnect (1s → 2s → 4s → … capped at 30s)
 * - Auto-resubscribe to fleet group on reconnect
 * - Connection state exposed via context
 */

import {
  createContext,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import {
  HubConnectionBuilder,
  LogLevel,
  type HubConnection,
} from "@microsoft/signalr";
import { env } from "@/lib/env";
import { useAuth } from "@/lib/auth-context";

// ─── Types ────────────────────────────────────────────────────────

export type SignalRStatus =
  | "disconnected"
  | "connecting"
  | "connected"
  | "reconnecting";

interface SignalRContextValue {
  /** Current connection state. */
  status: SignalRStatus;
  /** The raw hub connection (null while not built). */
  connection: HubConnection | null;
  /** Whether the hub has ever connected successfully in this session. */
  hasConnected: boolean;
}

const SignalRContext = createContext<SignalRContextValue | null>(null);

// ─── Provider ─────────────────────────────────────────────────────

export function SignalRProvider({ children }: { children: ReactNode }) {
  const { token } = useAuth();
  const [status, setStatus] = useState<SignalRStatus>("disconnected");
  const [hasConnected, setHasConnected] = useState(false);
  const [connection, setConnection] = useState<HubConnection | null>(null);
  const connectionRef = useRef<HubConnection | null>(null);
  const mountedRef = useRef(true);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  // Build, start, and manage the hub connection lifecycle
  useEffect(() => {
    if (!token) return;

    const connection = new HubConnectionBuilder()
      .withUrl(env.signalRHubUrl, {
        accessTokenFactory: () => token,
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          // Exponential backoff: 1s, 2s, 4s, 8s, 16s, 30s cap
          const delay = Math.min(
            1000 * Math.pow(2, retryContext.previousRetryCount),
            30_000,
          );
          return delay;
        },
      })
      .configureLogging(LogLevel.Information)
      .build();

    connectionRef.current = connection;

    // Wire up lifecycle callbacks
    connection.onreconnecting(() => {
      if (mountedRef.current) setStatus("reconnecting");
    });

    connection.onreconnected(async () => {
      if (mountedRef.current) {
        setStatus("connected");
        setHasConnected(true);
      }
      // Rejoin fleet group after reconnect
      try {
        await connection.invoke("JoinFleetGroup");
      } catch {
        // Will retry on next reconnect cycle
      }
    });

    connection.onclose(() => {
      if (mountedRef.current) setStatus("disconnected");
    });

    // Start the connection
    (async () => {
      try {
        if (mountedRef.current) setStatus("connecting");
        await connection.start();
        if (!mountedRef.current) return;
        setStatus("connected");
        setHasConnected(true);
        // Expose connection to consumers via state (async-safe)
        setConnection(connection);

        // Join the fleet group for broadcast updates
        await connection.invoke("JoinFleetGroup");
      } catch {
        if (mountedRef.current) setStatus("disconnected");
      }
    })();

    return () => {
      void connection.stop();
      connectionRef.current = null;
      setConnection(null);
      if (mountedRef.current) {
        setStatus("disconnected");
      }
    };
  }, [token]);

  return (
    <SignalRContext.Provider value={{ status, connection, hasConnected }}>
      {children}
    </SignalRContext.Provider>
  );
}

// ─── Hook ─────────────────────────────────────────────────────────

export function useSignalR(): SignalRContextValue {
  const ctx = useContext(SignalRContext);
  if (!ctx) {
    throw new Error("useSignalR must be used within a <SignalRProvider>");
  }
  return ctx;
}
