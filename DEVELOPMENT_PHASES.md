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
| 4 | **Frontend Dashboard** | [`frontend/`](frontend/README.md) | **Planned** — see [implementation phases](frontend/docs/01-implementation-phases.md) |
| 5 | Multi-tenancy & scale-out | — | Future |

### Phase 4 — Frontend (summary)

The dashboard consumes the BFF exclusively (REST + SignalR). Delivery is broken into six sub-phases (**F0–F5**):

| Sub-phase | Deliverable |
|---|---|
| F0 | Scaffold, auth, CI |
| F1 | Fleet summary + truck list |
| F2 | Live map + truck detail (SignalR) |
| F3 | Alerts feed + acknowledge |
| F4 | Performance, a11y, observability |
| F5 | Production release |

Full plan (PO / SO / Team Lead perspectives): **[frontend/docs/01-implementation-phases.md](frontend/docs/01-implementation-phases.md)**

**Entry gate:** BFF API contract and SignalR protocol must be ✅ Final before F0 starts ([BffApi/docs/10-roadmap.md §10.2](BffApi/docs/10-roadmap.md)).

---