# 03 — SignalR Protocol

> **Status:** 🟡 Draft
> **Audience:** Frontend (SignalR client), SRE
> **Goal:** Define the real-time contract over WebSockets — methods invoked by the server, methods invoked by the client, group semantics, payload schemas, and operational concerns (reconnect, backpressure).

---

## 3.1 Endpoints & transports

| Hub URL                          | Auth      | Transports (in preference order)                |
| -------------------------------- | --------- | ----------------------------------------------- |
| `wss://bff.fleetstream.example.com/hubs/v1/fleet` | JWT bearer | WebSockets, Server-Sent Events, Long Polling |

- **Negotiate endpoint:** `/hubs/v1/fleet/negotiate?negotiateVersion=1` — returns the connection token and chosen transport.
- **Client lib:** `@microsoft/signalr` 10.x on the dashboard.
- **Handshake:** the JWT is sent via the access_token query string OR `Authorization` header (negotiate and WebSocket upgrade both accept both).

---

## 3.2 Connection lifecycle

```
Client                                BFF
  │  GET /hubs/v1/fleet/negotiate     │
  │  Authorization: Bearer <jwt>     │
  │ ─────────────────────────────────▶│
  │ ◀──────────────────────────────  200 { connectionToken, connectionId, … }
  │                                   │
  │  WebSocket Upgrade                │
  │  Sec-WebSocket-Protocol: signalr │
  │ ─────────────────────────────────▶│
  │ ◀──────────────────────────────  101 Switching Protocols
  │                                   │
  │  {"type":1,"target":"OnConnected"}│
  │ ─────────────────────────────────▶│
  │                                   │
  │  ← server-to-client pushes        │
  │  {"type":1,"target":"OnTruckStateUpdate","arguments":[{"truckId":...}]}
  │                                   │
  │  {"type":6,"target":"Ping"}       │   every 15s (keep-alive)
  │ ◀─────────────────────────────────│
```

**Reconnect:** the SignalR client auto-reconnects with exponential backoff (1s, 2s, 4s, … capped at 30s). The BFF treats a reconnect as a **new** connection; the client MUST re-subscribe to groups on every `onreconnected` (see §3.4).

**Backpressure:** if the client falls behind, the server terminates the connection after `BackpressureWindow` (default **60 s** of un-acked sends). The client MUST reconnect and resubscribe.

---

## 3.3 Server → Client methods

All server-pushed methods are **strongly typed** via `IFleetHubClient` and `IAlertHubClient`. The TypeScript client is generated from the OpenAPI-adjacent `ts-signalr` schema or hand-written to match.

### `OnTruckStateUpdate(TruckState state)`

- **Frequency:** per truck, at most one update every **2 s** (server-side rate-limit per truck id).
- **Group:** `fleet` (all clients), `truck:{truckId}` (subscribers only).
- **Payload:** see `TruckState` in [02-api-contract.md §2.3](02-api-contract.md).

### `OnTelemetrySample(TelemetrySample sample)`

- **Frequency:** per truck, at most one every **5 s** by default; clients may opt into a higher rate by joining the `telemetry:full` group (admin only).
- **Group:** `telemetry:full` (default: only admins).

### `OnAlert(Alert alert)`

- **Group:** `alerts` (all clients).
- **Retention:** clients keep the last **500** alerts in a ring buffer; older ones are evicted on the server with a `OnAlertsPurged(count, beforeTimestamp)` notification.

### `OnFleetUpdate(IReadOnlyList<TruckState> states)`

- **Frequency:** every **5 s** when *any* state changed in the window; otherwise omitted.
- **Group:** `fleet` (all clients). Used for the dashboard's "warm reload" after SignalR reconnect.

### `OnPresenceChange(string truckId, bool isOnline)`

- **Trigger:** Kafka-driven sweeper sees no telemetry for `OnlineThreshold` (default 5 min).
- **Group:** `fleet`.

### `OnSystemMessage(string severity, string code, string message, DateTime timestamp)`

- **Group:** all clients. Used for ops events (planned maintenance, rate-limit notices, etc.).
- **Severity:** `info` | `warn` | `error`.

---

## 3.4 Client → Server methods

The client may invoke the following. Each takes at most one argument and returns `Task` (no return value).

| Method                            | Argument                  | Effect                                                   |
| --------------------------------- | ------------------------- | -------------------------------------------------------- |
| `JoinFleetGroup()`                | —                         | Adds the connection to the `fleet` group.                |
| `JoinTruckGroup(truckId)`         | string (≤ 64 chars)       | Adds to `truck:{truckId}` group. Idempotent.              |
| `LeaveTruckGroup(truckId)`        | string                    | Removes from `truck:{truckId}` group.                     |
| `JoinAlertsGroup()`               | —                         | Adds to `alerts` group. Default-on.                      |
| `RequestSnapshot()`               | —                         | Server replies with `OnFleetUpdate` immediately.         |
| `Ping()`                          | —                         | No-op; used for client-side liveness.                    |

**Default subscriptions:** on `OnConnectedAsync` the server adds the connection to:
- `user:{sub}` (for per-user targeting)
- `alerts` (always)
- `fleet` (always)

The client does **not** need to call `JoinFleetGroup()` unless it explicitly wants to re-join after a manual group leave.

---

## 3.5 Groups

| Group name              | Visibility | Join rules                                | Purpose                                  |
| ----------------------- | ---------- | ----------------------------------------- | ---------------------------------------- |
| `fleet`                 | All        | Auto on connect.                          | Broad fan-out of state updates.          |
| `alerts`                | All        | Auto on connect.                          | Alert fan-out.                           |
| `truck:{truckId}`       | Self       | Client invokes `JoinTruckGroup`.          | Per-truck deep-link view.                |
| `region:{regionCode}`   | Per-region | Server adds based on JWT claim `region`.  | Region dashboards.                       |
| `telemetry:full`        | Admins     | Server adds based on JWT role.            | High-rate telemetry stream.              |
| `user:{sub}`            | Self       | Auto on connect.                          | Targeted `SendToUser` (e.g. ack receipts). |
| `system:ops`            | Admins     | Manual.                                   | Operational broadcasts.                  |

Groups are **server-side only**; clients cannot enumerate or list other clients in a group.

---

## 3.6 Payload examples

### `OnTruckStateUpdate`

```json
{
  "truckId": "TAC-00342",
  "timestamp": "2026-08-29T12:34:56.789Z",
  "latitude": 37.7749,
  "longitude": -122.4194,
  "speedKmh": 52.4,
  "engineTemperatureCelsius": 88.1,
  "fuelLevelPercent": 64.8,
  "isMoving": true,
  "isOnline": true,
  "riskLevel": "Low",
  "riskScore": 12.5,
  "totalDistanceKm": 124.7,
  "violationsCount": 0,
  "anomaliesCount": 0
}
```

### `OnAlert`

```json
{
  "id": "alert-7b3a-…",
  "truckId": "TAC-00112",
  "alertType": "SpeedViolation",
  "severity": "Error",
  "message": "Truck TAC-00112 reported speed 142 km/h (limit 100).",
  "timestamp": "2026-08-29T12:35:01.103Z",
  "isAcknowledged": false,
  "metadata": { "speedKmh": 142, "limitKmh": 100, "geohash": "9q8yyz" }
}
```

### `OnSystemMessage`

```json
{
  "severity": "warn",
  "code": "summary.degraded",
  "message": "Fleet summary is being served from a stale cache (Redis warm-up).",
  "timestamp": "2026-08-29T12:36:00.000Z"
}
```

---

## 3.7 Operational behavior

- **Keep-alive:** server sends a `Ping` frame every **15 s**; clients that miss 3 pings reconnect.
- **Max message size:** 32 KiB (defensive). Anything bigger is rejected with `SignalRError.MessageTooLarge`.
- **Client timeout:** **60 s** of no inbound activity ⇒ server closes with code `1006`.
- **Burst limit:** at most **1,000** messages/sec to any single connection. Excess is dropped and counted in metric `signalr_messages_dropped_total{reason="burst"}`.
- **Backplane:** `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.x, channel prefix `FleetStream`. Cluster and Sentinel are both supported.
- **Sticky sessions:** not required. Backplane ensures any pod can serve any client.
- **Scaling:** horizontal scale of BFF pods is N; each pod can hold ≥ 10,000 connections. Total fan-out capacity = N × 10,000.
- **Replay window:** when a client reconnects, it does **not** receive missed messages automatically. It must call `RequestSnapshot()` and reconcile client-side. (We deliberately do not implement a replay buffer in Phase 3 to keep the hot path allocation-free.)

---

## 3.8 Failure handling

| Failure                                | Server behavior                                            | Client behavior (recommended)             |
| -------------------------------------- | ---------------------------------------------------------- | ----------------------------------------- |
| Client disconnects                     | Group memberships removed in `OnDisconnectedAsync`.         | Reconnect with backoff.                   |
| Redis backplane down                   | Server logs and continues; broadcasts within the same pod still work, cross-pod fan-out paused. | `RequestSnapshot()` on reconnect.         |
| Kafka consumer down                    | `OnTruckStateUpdate` stops; last snapshot remains.         | Surface a banner in the UI.               |
| Truck is in `Maintenance`              | Server still pushes updates but tags `isOnline: false`.     | Grey out in UI.                           |
| Message dropped due to backpressure    | Server sends `OnSystemMessage("warn", "backpressure", …)`. | Throttle the dashboard, request snapshot.  |

---

## 3.9 Acceptance criteria for this document

- [ ] A `WebApplicationFactory` test connects a `SignalR.Client`, subscribes to `fleet`, publishes a Kafka message, and asserts the `OnTruckStateUpdate` callback fires within 250 ms.
- [ ] A load test with **5,000** concurrent `SignalR.Client` connections on a 2-core pod sustains 1,000 broadcasts/sec with p99 < 100 ms.
- [ ] A reconnect test simulates a 30 s Kafka outage and asserts the client recovers via `RequestSnapshot()` without data loss.
- [ ] A `Verify` snapshot test pins the `IFleetHubClient` shape so accidental renames break the build.
