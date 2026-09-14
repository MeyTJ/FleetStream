# FleetStream Application Development Phases

## Overview

FleetStream is a high-throughput IoT fleet telemetry platform that ingests, processes, and visualizes real-time telemetry data (GPS coordinates, engine temperature, speed) from 10,000+ delivery trucks. This document outlines the phased development approach for building this system, mirroring financial market data architecture patterns.

## Development Philosophy

- **Iterative Approach**: Build incrementally, validating each phase before moving to the next
- **End-to-End Validation**: Each phase should be independently testable and demonstrable
- **Technology Diversity**: Each phase showcases different expertise areas (Go, Kafka, .NET, Frontend)
- **Production-Ready**: All components should be designed for scalability, reliability, and maintainability

---

## Platform phases

| Phase | Application | Directory | Status |
|---|---|---|---|
| 1 | Ingress Gateway | [`ingress-gateway/`](ingress-gateway/README.md) | Complete |
| 2 | Streaming Engine | [`streaming-engine/`](streaming-engine/README.md) | Complete |
| 3 | BFF API | [`BffApi/`](BffApi/README.md) | Code-complete; verification pending |
| 4 | **Frontend Dashboard** | [`frontend/`](frontend/README.md) | **F0–F3 implemented; F4–F5 open** — see [implementation phases](frontend/docs/01-implementation-phases.md) and [measured readiness](docs/frontend/production-readiness-checklist.md) |
| 5 | Multi-tenancy & scale-out | — | Future |

### Phase 4 — Frontend (summary)

The dashboard consumes the BFF exclusively (REST + SignalR). Delivery is broken into six sub-phases (**F0–F5**):

| Sub-phase | Deliverable | Status (2026-09-14) |
|---|---|---|
| F0 | Scaffold, auth, CI | ✅ Done (dev-token auth; no OIDC yet) |
| F1 | Fleet summary + truck list | ✅ Done |
| F2 | Live map + truck detail (SignalR) | ✅ Done — clustered map, snapshot-on-reconnect |
| F3 | Alerts feed + acknowledge | ✅ Done — virtualized feed |
| F4 | Performance, a11y, observability | 🟡 Partial — scale fixes done; E2E, axe/Lighthouse gates and client error reporting missing |
| F5 | Production release | 🔴 Open — Dockerfile and K8s manifests added, image build unverified (no Docker daemon locally) |

Full plan (PO / SO / Team Lead perspectives): **[frontend/docs/01-implementation-phases.md](frontend/docs/01-implementation-phases.md)**

**Entry gate:** BFF API contract and SignalR protocol must be ✅ Final before F0 starts ([BffApi/docs/10-roadmap.md §10.2](BffApi/docs/10-roadmap.md)).
**Note:** F0–F3 were built *ahead* of this gate, and the gate is still unsatisfied — [03-signalr-protocol.md §3.3](BffApi/docs/03-signalr-protocol.md) specifies `OnAlertsPurged`, `OnPresenceChange` and `OnSystemMessage`, none of which the server ever sends. Either implement them or mark them deferred before F5 sign-off.

---