"use client";

/**
 * SignalR hub connection provider for FleetStream.
 *
 * - Singleton hub connection per app lifecycle
 * - Exponential backoff reconnect (1s → 2s → 4s → … capped at 30s)
 * - Re-joins per-truck groups and pulls a state snapshot on reconnect
 *   (protocol §3.2/§3.4 — the BFF treats a reconnect as a *new* connection;
 *   `fleet`/`alerts` are re-added server-side in OnConnectedAsync, so the
 *   explicit JoinFleetGroup below is an idempotent safety net)
 * - Connection state exposed via context
 */

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import {
  HubConnectionBuilder,
  HubConnectionState,
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
  /**
   * Join a per-truck group, remembering the membership so it is re-established
   * automatically after a reconnect. No-op until the hub is connected.
   */
  joinTruckGroup: (truckId: string) => void;
  /** Drop a per-truck group from the tracked membership. */
  leaveTruckGroup: (truckId: string) => void;
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
  // Per-truck groups the UI currently cares about. The server drops group
  // membership when a connection dies, so this set drives re-joins after a
  // reconnect (protocol §3.2: a reconnect is a *new* connection).
  const truckGroupsRef = useRef<Set<string>>(new Set());

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  /**
   * Re-establish every subscription this client relies on, then pull a fresh
   * snapshot. Safe to call on initial connect and on every reconnect (§3.4).
   */
  const resubscribe = useCallback(async (conn: HubConnection) => {
    try {
      await conn.invoke("JoinFleetGroup");
    } catch {
      // Server auto-joins `fleet` in OnConnectedAsync; an explicit failure here
      // must not block the snapshot or the per-truck re-joins below.
    }
    for (const truckId of truckGroupsRef.current) {
      try {
        await conn.invoke("JoinTruckGroup", truckId);
      } catch {
        // Retried on the next reconnect cycle.
      }
    }
    try {
      // §7 "Replay window": missed messages are never replayed, so the only way
      // to converge after a gap is to ask the server for the current fleet.
      await conn.invoke("RequestSnapshot");
    } catch {
      // Fall back to REST reconciliation; the reconnect banner still shows.
    }
  }, []);

  const joinTruckGroup = useCallback((truckId: string) => {
    truckGroupsRef.current.add(truckId);
    const conn = connectionRef.current;
    if (conn?.state === HubConnectionState.Connected) {
      void conn.invoke("JoinTruckGroup", truckId).catch(() => {
        // Will be re-joined on the next reconnect via resubscribe().
      });
    }
  }, []);

  const leaveTruckGroup = useCallback((truckId: string) => {
    truckGroupsRef.current.delete(truckId);
    const conn = connectionRef.current;
    if (conn?.state === HubConnectionState.Connected) {
      void conn.invoke("LeaveTruckGroup", truckId).catch(() => {
        // Membership dies with the connection anyway.
      });
    }
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
      // Protocol §3.2/§3.4: the BFF treats a reconnected client as a brand-new
      // connection, so group membership lost with the old connection must be
      // re-established and the missed state replayed via RequestSnapshot().
      await resubscribe(connection);
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

        // Join the fleet group and pull the initial snapshot (§3.4).
        await resubscribe(connection);
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
  }, [token, resubscribe]);

  const value = useMemo<SignalRContextValue>(
    () => ({
      status,
      connection,
      hasConnected,
      joinTruckGroup,
      leaveTruckGroup,
    }),
    [status, connection, hasConnected, joinTruckGroup, leaveTruckGroup],
  );

  return (
    <SignalRContext.Provider value={value}>
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
