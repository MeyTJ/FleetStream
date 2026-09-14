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

## Scripts

| Command | Description |
|---|---|
| `npm run dev` | Start dev server on `http://localhost:3000` |
| `npm run build` | Production build |
| `npm run start` | Serve the production build |
| `npm run lint` | ESLint (`eslint-config-next`, core-web-vitals + TS) |
| `npm run typecheck` | `tsc --noEmit` (strict, no emit) |

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
- **Live updates** — `SignalRProvider` connects to `/hubs/v1/fleet`, auto-joins the `fleet` + `alerts` groups, and reconnects with exponential backoff (1 s → 30 s cap), re-invoking `JoinFleetGroup` on reconnect. `OnTruckStateUpdate` / `OnFleetUpdate` / `OnAlert` / `OnAlertsPurged` feed the external stores.
- **Reconnect UX** — `ReconnectBanner` surfaces connection state; stores keep last-known data so the UI never blanks.
- **Telemetry history** — the truck detail page renders a 24 h sparkline from `GET /api/v1/fleet/trucks/{id}/telemetry` (speed / engine temp / fuel / risk score), with a compact variant in the map detail panel.
- **Alerts & acks** — REST initial load merges into a client ring buffer (500 max); `OnAlert` prepends live entries. Ack is optimistic with rollback on failure, and the Ack button is role-gated to mirror the BFF `AlertsAck` policy (`alerts:ack` or `fleet:admin`).
- **Errors** — the API client parses RFC 7807 `ProblemDetails`; `ErrorState` renders title/detail with correlation + trace IDs and a retry action.

## CI

[`.github/workflows/frontend.yml`](../.github/workflows/frontend.yml) runs on pushes/PRs touching `frontend/**`:

1. `npm ci`
2. `npm run lint`
3. `npm run typecheck`
4. `npm run build`

## Docs

- [Implementation phases (F0–F5)](docs/01-implementation-phases.md)
- [BFF API contract](../BffApi/docs/02-api-contract.md)
- [SignalR protocol](../BffApi/docs/03-signalr-protocol.md)
