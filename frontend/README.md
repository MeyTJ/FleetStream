# FleetStream Dashboard (Frontend)

Real-time fleet operations dashboard for FleetStream — the Phase 4 Next.js frontend that visualizes telemetry, truck states, and alerts from the [BFF API](../BffApi/). Built for 10,000+ trucks with live SignalR updates and REST polling fallback.

## Stack

| Concern | Choice |
|---|---|
| Framework | Next.js 16 (App Router, React 19, strict TypeScript) |
| Server state | TanStack Query v5 (5 s stale time, 10 s polling) |
| Real-time | `@microsoft/signalr` 10.x (`/hubs/v1/fleet`) |
| Map | MapLibre GL (no API key, CSP-safe tiles) |
| Styling | Tailwind CSS v4 |
| Icons | lucide-react |

## Prerequisites

- **Node.js 24+** and npm 11+ (matches CI — see [`.github/workflows/frontend.yml`](../.github/workflows/frontend.yml))
- **FleetStream BFF API** running locally on `http://localhost:8080` (see [BffApi/README.md](../BffApi/README.md) or use `docker compose` from the repo root)

## Getting started

```bash
cd frontend
npm ci
cp .env.example .env.local   # adjust URLs if the BFF runs elsewhere
npm run dev
```

Open [http://localhost:3000](http://localhost:3000) and sign in with any operator ID. Login uses the BFF dev-token endpoint (`POST /api/v1/auth/dev-token`, Development-only) and a role preset:

| Preset | Roles | Effect |
|---|---|---|
| Operator | `fleet:reader`, `alerts:ack` | Read fleet + acknowledge alerts |
| Viewer | `fleet:reader` | Read-only — Ack button hidden |
| Admin | `fleet:admin` | Full access (implies reader + ack, joins `telemetry:full`) |

## Environment variables

See [`.env.example`](.env.example); copy to `.env.local` for local development. All values are `NEXT_PUBLIC_` (browser-exposed by design — they contain no secrets; the JWT lives in `sessionStorage` for the dev flow and is validated server-side on every call).

| Variable | Default | Purpose |
|---|---|---|
| `NEXT_PUBLIC_API_BASE_URL` | `http://localhost:8080` | BFF REST base URL |
| `NEXT_PUBLIC_SIGNALR_HUB_URL` | `http://localhost:8080/hubs/v1/fleet` | SignalR hub URL |
| `CSP_CONNECT_SRC` | *(unset)* | Optional space-separated override for the CSP `connect-src` directive. Unset = derived from the two variables above, so a staging/prod build automatically allows its own BFF over both `https://` and `wss://`. Set it only when the browser must reach extra origins (e.g. a separate websocket edge host). |

The CSP is built at **build** time (`src/lib/csp.ts`, unit-tested in
`src/lib/csp.test.ts`), so `NEXT_PUBLIC_*` values must be present in the
environment where `npm run build` (or `docker build --build-arg`) runs —
setting them only on the running container has no effect.

## Scripts

| Command | Description |
|---|---|
| `npm run dev` | Start dev server on `http://localhost:3000` |
| `npm run build` | Production build |
| `npm run start` | Serve the production build |
| `npm run lint` | ESLint (`eslint-config-next`, core-web-vitals + TS) |
| `npm run typecheck` | `tsc --noEmit` (strict, no emit) |
| `npm test` | Vitest unit tests (jsdom + Testing Library) |
| `npm run test:watch` | Vitest in watch mode |

## Project structure

```
frontend/
├── src/
│   ├── app/
│   │   ├── (dashboard)/        # Authenticated shell: dashboard, trucks, map, alerts, settings
│   │   │   ├── trucks/[truckId]/   # Truck detail: live state + 24 h telemetry sparkline
│   │   │   └── alerts/            # Live alert feed + acknowledge
│   │   └── login/             # Dev-token login with role presets
│   ├── components/            # Feature components (map, tables, alerts, sparkline, banners)
│   └── lib/
│       ├── api-client.ts      # Typed fetch wrapper: JWT injection, RFC 7807 parsing, retries
│       ├── auth-context.tsx   # Dev-token auth state (sessionStorage)
│       ├── signalr-provider.tsx  # Hub connection lifecycle, backoff reconnect, group rejoin
│       ├── hooks/fleet.ts     # TanStack Query hooks per BFF resource
│       ├── hooks/signalr-events.ts  # Server → store wiring (states, alerts, purge)
│       ├── alert-store.ts     # Ring buffer (500) + optimistic ack + purge trim
│       ├── truck-state-store.ts  # Live states (throttle 1/truck/2 s)
│       └── jwt.ts             # Client-side role claims for UI gating (not a security boundary)
├── docs/                      # Phase plan and notes
└── .env.example
```

## How it works

- **REST bootstrap** — TanStack Query fetches summary, trucks, state, telemetry, and alerts (`GET /api/v1/fleet/*`), with cursor pagination on trucks/alerts.
- **Live updates** — `SignalRProvider` is mounted once in the dashboard layout (so the hub connection survives navigation between routes) and connects to `/hubs/v1/fleet` with exponential backoff reconnect (1 s → 30 s cap). On every reconnect it re-joins the fleet group, re-joins any per-truck groups the UI still needs, and invokes `RequestSnapshot()` — protocol §3.2/§3.7 require this because the server never replays missed messages. `OnTruckStateUpdate` / `OnFleetUpdate` / `OnAlert` / `OnTelemetrySample` / `OnAlertsPurged` feed the external stores.
- **Admin telemetry** — the server adds `fleet:admin` connections to the `telemetry:full` group, so admins receive `OnTelemetrySample` pushes. These land in `telemetry-sample-store.ts` and are merged into the truck sparkline (marked "live"). Non-admin sessions never receive the event and fall back to the 24 h REST history, as specified.
- **Reconnect UX** — `ReconnectBanner` surfaces connection state; stores keep last-known data so the UI never blanks.
- **Telemetry history** — the truck detail page renders a 24 h sparkline from `GET /api/v1/fleet/trucks/{id}/telemetry` (speed / engine temp / fuel / risk score), with a compact variant in the map detail panel.
- **Alerts & acks** — REST initial load merges into a client ring buffer (500 max); `OnAlert` prepends live entries. The feed is virtualized (`@tanstack/react-virtual`) so only visible rows render. Ack is optimistic with rollback on failure, and the Ack button is role-gated to mirror the BFF `AlertsAck` policy (`alerts:ack` or `fleet:admin`).
- **Map rendering** — the fleet is drawn from one clustered MapLibre GeoJSON source rather than a DOM marker per truck, keeping update cost flat as the fleet grows. Clicking a cluster zooms to expand it.
- **Errors** — the API client parses RFC 7807 `ProblemDetails`; `ErrorState` renders title/detail with correlation + trace IDs and a retry action.

## Tests

`npm test` runs Vitest (jsdom + Testing Library) over `src/**/*.test.{ts,tsx}`:

| Suite | Covers |
|---|---|
| `src/lib/csp.test.ts` | CSP/`connect-src` derivation, including the "no localhost leak" guard |
| `src/lib/signalr-provider.test.tsx` | connect/reconnect subscription lifecycle, snapshot request, per-truck group re-join |
| `src/lib/stores.test.tsx` | alert ring buffer + purge, truck-state throttle/ordering, telemetry sample ring |

Config lives in `vitest.config.mts`; shared setup in `src/test/setup.ts`.

## Docker

```bash
docker build \
  --build-arg NEXT_PUBLIC_API_BASE_URL=https://api.fleetstream.example.com \
  --build-arg NEXT_PUBLIC_SIGNALR_HUB_URL=https://api.fleetstream.example.com/hubs/v1/fleet \
  -t fleetstream/frontend:latest .
docker run --rm -p 3000:3000 fleetstream/frontend:latest
```

The image is multi-stage and runs the `output: "standalone"` server as a
non-root user on port 3000. Kubernetes manifests:
[`ops/k8s/frontend/deployment.yaml`](../ops/k8s/frontend/deployment.yaml); the
dashboard is exposed at `fleetstream.example.com` in
[`ops/k8s/ingress.yaml`](../ops/k8s/ingress.yaml).

## CI

[`.github/workflows/frontend.yml`](../.github/workflows/frontend.yml) runs on pushes/PRs touching `frontend/**`:

1. `npm ci`
2. `npm run lint`
3. `npm run typecheck`
4. `npm test`
5. `npm run build`

A second job builds the Docker image for a non-local origin and asserts the
served CSP contains that origin and **not** `localhost` — the regression that
previously made the frontend undeployable.

## Docs

- [Implementation phases (F0–F5)](docs/01-implementation-phases.md)
- [BFF API contract](../BffApi/docs/02-api-contract.md)
- [SignalR protocol](../BffApi/docs/03-signalr-protocol.md)
