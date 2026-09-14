import { act, renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

// A recording fake for the SignalR client: the provider only needs the builder
// chain plus invoke/on/state, so these tests assert on the protocol calls the
// provider makes rather than on a real socket.
const { fakeConnection, invokes, listeners, builderCalls } = vi.hoisted(() => {
  const invokes: string[][] = [];
  const listeners: Record<string, Array<(...args: unknown[]) => unknown>> = {};
  const builderCalls: string[] = [];
  const fakeConnection = {
    state: "Connected" as string,
    invoke: (...args: unknown[]) => {
      invokes.push(args.map(String));
      return Promise.resolve();
    },
    on: (name: string, cb: (...args: unknown[]) => unknown) => {
      listeners[name] = [...(listeners[name] ?? []), cb];
    },
    off: (name: string) => {
      delete listeners[name];
    },
    onreconnecting: (cb: () => unknown) => {
      listeners["reconnecting"] = [cb];
    },
    onreconnected: (cb: () => unknown) => {
      listeners["reconnected"] = [cb];
    },
    onclose: (cb: () => unknown) => {
      listeners["close"] = [cb];
    },
    start: () => {
      builderCalls.push("start");
      return Promise.resolve();
    },
    stop: () => Promise.resolve(),
  };
  return { fakeConnection, invokes, listeners, builderCalls };
});

vi.mock("@microsoft/signalr", () => ({
  HubConnectionState: { Connected: "Connected", Disconnected: "Disconnected" },
  LogLevel: { Information: 1, Error: 3 },
  HubConnectionBuilder: class {
    withUrl() {
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      builderCalls.push("build");
      return fakeConnection;
    }
  },
}));

const authRef = vi.hoisted(() => ({ token: "test-token" }));
vi.mock("@/lib/auth-context", () => ({
  useAuth: () => ({ token: authRef.token }),
}));

import { SignalRProvider, useSignalR } from "./signalr-provider";

function wrapper({ children }: { children: ReactNode }) {
  return <SignalRProvider>{children}</SignalRProvider>;
}

function invocationsOf(method: string): string[][] {
  return invokes.filter((call) => call[0] === method);
}

async function waitForInitialSubscribe() {
  await waitFor(() => {
    expect(invokes.some((c) => c[0] === "RequestSnapshot")).toBe(true);
  });
}

beforeEach(() => {
  invokes.length = 0;
  builderCalls.length = 0;
  for (const key of Object.keys(listeners)) delete listeners[key];
  authRef.token = "test-token";
  fakeConnection.state = "Connected";
});

describe("SignalRProvider subscription lifecycle", () => {
  it("requests a snapshot after the initial connect", async () => {
    renderHook(() => useSignalR(), { wrapper });
    await waitForInitialSubscribe();

    // Protocol §3.4: RequestSnapshot is the only way to converge, because the
    // server never replays missed messages (§7 "Replay window").
    expect(invocationsOf("JoinFleetGroup").length).toBeGreaterThanOrEqual(1);
    expect(invocationsOf("RequestSnapshot").length).toBeGreaterThanOrEqual(1);
  });

  it("re-joins the fleet group and re-snapshots after a reconnect", async () => {
    renderHook(() => useSignalR(), { wrapper });
    await waitForInitialSubscribe();

    const snapshotCallsBefore = invocationsOf("RequestSnapshot").length;
    const joinCallsBefore = invocationsOf("JoinFleetGroup").length;

    await act(async () => {
      await listeners.reconnected?.[0]?.();
    });

    expect(invocationsOf("JoinFleetGroup").length).toBe(joinCallsBefore + 1);
    expect(invocationsOf("RequestSnapshot").length).toBe(
      snapshotCallsBefore + 1,
    );
  });

  it("re-joins tracked per-truck groups after a reconnect", async () => {
    const { result } = renderHook(() => useSignalR(), { wrapper });
    await waitForInitialSubscribe();

    act(() => {
      result.current.joinTruckGroup("truck-42");
    });
    expect(invocationsOf("JoinTruckGroup")).toEqual([
      ["JoinTruckGroup", "truck-42"],
    ]);

    await act(async () => {
      await listeners.reconnected?.[0]?.();
    });

    // The membership is remembered, so the reconnect re-joins it.
    expect(invocationsOf("JoinTruckGroup")).toEqual([
      ["JoinTruckGroup", "truck-42"],
      ["JoinTruckGroup", "truck-42"],
    ]);
  });

  it("stops re-joining a truck group once it has been left", async () => {
    const { result } = renderHook(() => useSignalR(), { wrapper });
    await waitForInitialSubscribe();

    act(() => {
      result.current.joinTruckGroup("truck-7");
      result.current.leaveTruckGroup("truck-7");
    });
    const before = invocationsOf("JoinTruckGroup").length;

    await act(async () => {
      await listeners.reconnected?.[0]?.();
    });
    expect(invocationsOf("JoinTruckGroup")).toHaveLength(before);
  });

  it("still snapshots even when a group join is rejected", async () => {
    renderHook(() => useSignalR(), { wrapper });
    await waitForInitialSubscribe();

    const original = fakeConnection.invoke;
    fakeConnection.invoke = (...args: unknown[]) => {
      if (args[0] === "JoinFleetGroup") {
        return Promise.reject(new Error("policy denied"));
      }
      return original(...args);
    };

    await act(async () => {
      await listeners.reconnected?.[0]?.();
    });
    fakeConnection.invoke = original;

    // A failed join must not strand the client on stale state.
    expect(invocationsOf("RequestSnapshot").length).toBeGreaterThanOrEqual(2);
  });

  it("does not build a connection when there is no auth token", async () => {
    authRef.token = "";
    const { result } = renderHook(() => useSignalR(), { wrapper });
    await waitFor(() => {
      expect(result.current.status).toBe("disconnected");
    });
    expect(builderCalls).not.toContain("build");
  });

  it("surfaces reconnecting status without losing hasConnected", async () => {
    const { result } = renderHook(() => useSignalR(), { wrapper });
    await waitForInitialSubscribe();

    act(() => {
      listeners.reconnecting?.[0]?.();
    });
    expect(result.current.status).toBe("reconnecting");
    expect(result.current.hasConnected).toBe(true);
  });
});

