# FleetStream Frontend (Phase 4)

Real-time fleet telemetry dashboard — the public-facing UI for FleetStream operators.

## Role in the platform

The frontend is the **only user-facing application** in FleetStream. It consumes the [BFF API](../BffApi/README.md) exclusively via REST and SignalR. It does not call Ingress Gateway or Streaming Engine directly.

```
Trucks → Ingress → Kafka → Streaming → Redis/Kafka → BFF → Frontend (this app)
```

## Technology stack (planned)

| Layer | Choice | Rationale |
|---|---|---|
| Framework | Next.js 15 (App Router) | SSR for auth shell; RSC for static layout; client islands for real-time widgets |
| Language | TypeScript (strict) | Contract safety with generated API types |
| Real-time | `@microsoft/signalr` 10.x | Matches BFF SignalR protocol |
| Maps | MapLibre GL or Leaflet | Open-source; no vendor lock-in for fleet markers |
| Styling | Tailwind CSS + shadcn/ui | Consistent design system; accessible primitives |
| State | TanStack Query + Zustand | Server state vs. ephemeral UI / SignalR buffer |
| Auth | JWT bearer (dev token / OIDC) | Aligns with [BFF security spec](../BffApi/docs/05-security.md) |

## Documentation

| Document | Description |
|---|---|
| [docs/README.md](docs/README.md) | Specification index |
| [docs/01-implementation-phases.md](docs/01-implementation-phases.md) | **Phased delivery plan** — PO, SO, and Team Lead perspectives |
| [docs/02-architecture.md](docs/02-architecture.md) | Frontend architecture (planned) |
| [docs/03-bff-integration.md](docs/03-bff-integration.md) | REST + SignalR integration contract (planned) |

## Prerequisites

Phase 4 starts when the BFF Phase 3 → Phase 4 contract is satisfied ([BffApi/docs/10-roadmap.md §10.2](../BffApi/docs/10-roadmap.md)):

- [ ] `02-api-contract.md` and `03-signalr-protocol.md` are ✅ Final
- [ ] BFF image tagged `v1.0.0` available in registry
- [ ] OpenAPI document reachable from CI (`/swagger/v1/swagger.json`)

## Local development (once scaffolded)

```bash
cd frontend
npm install
cp .env.example .env.local
npm run dev
```

Default BFF target: `http://localhost:8080` (root [docker-compose.yml](../docker-compose.yml), `dev` profile).

## Related

- [DEVELOPMENT_PHASES.md](../DEVELOPMENT_PHASES.md) — platform-wide phase plan
- [BffApi/docs/02-api-contract.md](../BffApi/docs/02-api-contract.md) — REST surface
- [BffApi/docs/03-signalr-protocol.md](../BffApi/docs/03-signalr-protocol.md) — WebSocket contract
