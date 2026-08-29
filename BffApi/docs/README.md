# FleetStream BFF API — Specification Suite

> **Phase 3 deliverable:** Production-grade specifications for the Backend-for-Frontend (BFF) API of the FleetStream IoT Fleet Telemetry Platform.
>
> **Target framework:** .NET 10 — see [01-architecture.md](01-architecture.md) for the rationale and the net10.0 → package matrix.
>
> **Companion documents:**
> - [../DEVELOPMENT_PHASES.md](../../DEVELOPMENT_PHASES.md) — project-level phase plan (Phases 1–5)
> - [../README.md](../README.md) — repo entry point and quick-start

---

## Document Index

| #   | Document                          | Scope                                                                 |
| --- | --------------------------------- | --------------------------------------------------------------------- |
| 01  | [Architecture](01-architecture.md)        | System context, clean-architecture layers, deployment topology, runtime data flow, technology stack. |
| 02  | [API Contract](02-api-contract.md)        | REST surface: endpoints, request/response schemas, error model, pagination, OpenAPI metadata, versioning. |
| 03  | [SignalR Protocol](03-signalr-protocol.md)| WebSocket hub contract: client/server methods, group semantics, payload schemas, heartbeat, reconnection. |
| 04  | [Data Model](04-data-model.md)            | Domain entities, DTOs, Redis keyspace design, Kafka topic contracts, idempotency, retention. |
| 05  | [Security](05-security.md)                | JWT authentication, CORS, rate limiting, input validation, secrets, threat model. |
| 06  | [Configuration](06-configuration.md)      | `appsettings.json` schema, environment-variable overrides, feature flags, defaults. |
| 07  | [Observability](07-observability.md)      | Structured logging, OpenTelemetry traces/metrics, health checks, SLOs, alert hooks. |
| 08  | [Deployment](08-deployment.md)            | Docker image, `docker-compose.yml` topology, Redis HA backplane, scaling, blue/green. |
| 09  | [Testing](09-testing.md)                  | Test pyramid, unit/integration/contract/E2E test specs, coverage targets, CI gates. |
| 10  | [Roadmap](10-roadmap.md)                  | Phase 3 milestones, exit criteria, risks, dependencies, decision log. |

---

## How to read this suite

1. **Newcomers** — read `01-architecture.md`, then `02-api-contract.md`, then `04-data-model.md`.
2. **Backend implementers** — `01`, `02`, `03`, `04`, `06`, `09`.
3. **Frontend / dashboard (Phase 4) implementers** — `02`, `03`, `04`.
4. **Platform / SRE** — `05`, `07`, `08`, `10`.
5. **Reviewers / hiring managers** — `README.md` (this file) + `10-roadmap.md` give the executive picture.

---

## Conventions used in these specs

- **RFC 2119 keywords** (`MUST`, `SHOULD`, `MAY`) are normative.
- **Code blocks** are authoritative for schemas; human prose is descriptive.
- **Versioning** — every contract follows `vMAJOR.MINOR`. Breaking changes increment `MAJOR`.
- **Casing** — JSON is `camelCase`; C# code is `PascalCase`; Redis keys are `kebab:snake_case`; Kafka topics are `kebab.case.dotted`.
- **Timestamps** — all wire timestamps are ISO 8601 UTC (`2026-08-29T12:34:56.789Z`); Unix-ms epoch only appears in legacy Kafka payloads.
- **IDs** — truck IDs are opaque strings (UUIDv4 or vendor-assigned), max 64 chars, URL-safe.

---

## Status legend

| Badge   | Meaning                                          |
| ------- | ------------------------------------------------ |
| ✅ Final | Reviewed and stable for the current milestone.   |
| 🟡 Draft | Open issues; track in `10-roadmap.md` §Decisions.|
| 🔴 TBD  | Not yet drafted; placeholder.                    |

Document status: 🟡 Draft — review target EOM of Milestone M3.
