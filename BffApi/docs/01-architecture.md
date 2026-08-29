# 01 — Architecture

> **Status:** 🟡 Draft
> **Audience:** Engineers, SRE, security, hiring reviewers
> **Goal:** Define the runtime topology, clean-architecture layers, data flow, and the non-negotiable technology choices for the FleetStream BFF API.

---

## 1.1 System context

FleetStream is a high-throughput IoT platform ingesting telemetry from **10,000+ delivery trucks** (GPS, engine temperature, speed, fuel). The platform is decomposed into five independently deliverable phases (see [DEVELOPMENT_PHASES.md](../../DEVELOPMENT_PHASES.md)).

The **BFF API** (this service) is the **only** public-facing HTTP/WS surface that the Phase 4 dashboard talks to. It does not own ingestion or stream processing — it composes the data those services produce.

```
                ┌──────────────────────────────────────────────┐
                │              Phase 4 — Dashboard             │
                │   Next.js + TypeScript + SignalR client      │
                └────────────────┬─────────────────────────────┘
                                 │ HTTPS / WSS
                                 ▼
        ┌────────────────────────────────────────────────────────┐
        │           Phase 3 — BFF API (this service)              │
        │  ASP.NET Core 10 · Clean Architecture · YARP · SignalR  │
        └───┬───────────────────┬─────────────────────┬──────────┘
            │                   │                     │
   YARP proxy / direct     Kafka (consume)        Redis HA
            │                   │                     │
            ▼                   ▼                     ▼
  ┌──────────────────┐  ┌───────────────────┐  ┌────────────────┐
  │ Phase 1 Ingress  │  │ Phase 2 Streaming │  │ Redis Sentinel │
  │ Go · gRPC+WS     │  │ Engine · Kafka    │  │ / Cluster      │
  │ (control plane)  │  │ (processed topic) │  │ (state store)  │
  └──────────────────┘  └───────────────────┘  └────────────────┘
```

**Out of scope** for the BFF:

- Owning the canonical `Truck` CRUD store — that lives in the Ingress Gateway persistence layer. The BFF keeps a **read-side cache** (Redis).
- Re-processing telemetry. We consume **already-processed** events only.
- Authoritative anomaly detection. The BFF surfaces what the Streaming Engine decided.

---

## 1.2 Quality attribute targets (Phase 3 exit criteria)

| Attribute              | Target                                                                  |
| ---------------------- | ----------------------------------------------------------------------- |
| **Latency, p50**       | < 50 ms for cached reads; < 150 ms for cold reads.                      |
| **Latency, p99**       | < 300 ms for cached reads; < 500 ms for cold reads.                     |
| **Throughput**         | ≥ 5,000 RPS sustained on summary endpoint (read-heavy, cache-warm).     |
| **SignalR fan-out**    | ≥ 10,000 concurrent WS clients per pod; ≥ 1,000 broadcasts/sec.         |
| **Availability**       | 99.9% monthly (≈ 43 min/month budget).                                  |
| **Deploy time**        | Rolling restart < 60 s; cold start < 10 s.                              |
| **Recovery**           | Pod kill → reconnect < 5 s for existing WS clients.                     |
| **Build time**         | `dotnet publish` < 30 s on a 4-core developer laptop.                   |

---

## 1.3 Clean Architecture layering

The four-project layout **MUST** be preserved. The dependency rule is one-way: `Presentation → Application → Core ← Infrastructure`.

```
            ┌──────────────────────────────────────────┐
            │  Presentation  (FleetStream.Presentation) │
            │  ASP.NET Core 10 host. Controllers, Hubs, │
            │  Middleware, Program.cs, DI composition. │
            └────────────────────┬─────────────────────┘
                                 │ references
            ┌────────────────────▼─────────────────────┐
            │  Application  (FleetStream.Application)  │
            │  Use-cases, DTOs, MediatR handlers,      │
            │  FluentValidation rules, interfaces.     │
            └────────────────────┬─────────────────────┘
                                 │ references
            ┌────────────────────▼─────────────────────┐
            │  Core  (FleetStream.Core)                │
            │  Pure domain: entities, value objects,   │
            │  domain events, no I/O.                  │
            └────────────────────▲─────────────────────┘
                                 │ implemented by
            ┌────────────────────┴─────────────────────┐
            │  Infrastructure  (FleetStream.Infrastructure) │
            │  Redis, Kafka, SignalR, YARP, repos,    │
            │  outbox/idempotency adapters.            │
            └──────────────────────────────────────────┘
```

### Project responsibilities

| Project               | Contains                                                                                            | MUST NOT contain                              |
| --------------------- | --------------------------------------------------------------------------------------------------- | --------------------------------------------- |
| `Core`                | Entities (`Truck`, `TruckState`, `Alert`, …), value objects, domain events, domain exceptions.      | Any reference to `Microsoft.*` or `System.Net*` or JSON serializers. |
| `Application`         | `IFleetQueryService`, MediatR requests/handlers/validators, DTOs, mapping profiles.                 | EF Core, Redis, Kafka, SignalR, HTTP types.   |
| `Infrastructure`      | `RedisTruckStateStore`, `RedisCacheService`, `KafkaTelemetryConsumer`, `SignalRNotificationService`, `InMemoryTruckRepository`. | ASP.NET Core types (`ControllerBase`, `Hub`, `IConfiguration`). |
| `Presentation`        | `Program.cs` (composition root), `FleetController`, `FleetHub`, middleware, `appsettings.json`.     | Business rules, direct Redis/Kafka calls.     |

**Why this matters for reviewers:** the dependency rule makes the Core unit-testable in milliseconds with zero infrastructure. The Application layer is testable with hand-rolled fakes. The cost of getting layering wrong is **inability to swap Redis for DragonflyDB or Kafka for Pulsar** without rewriting controllers.

---

## 1.4 Runtime topology (single pod)

```
  ┌──────────────────────────────────────────────────────────────┐
  │  ASP.NET Core 10 process  (Linux container, non-root)        │
  │                                                              │
  │  Kestrel (HTTP/1.1, HTTP/2, HTTP/3)                          │
  │      │                                                       │
  │      ├─ /api/*        →  Controllers  →  MediatR pipeline    │
  │      │                              ├─ ValidationBehavior    │
  │      │                              ├─ LoggingBehavior       │
  │      │                              └─ CachingBehavior       │
  │      │                                                       │
  │      ├─ /hubs/fleet   →  SignalR Hub  →  Redis Backplane     │
  │      │                                                       │
  │      ├─ /api/ingress  →  YARP  →  Ingress Gateway (gRPC-Web) │
  │      ├─ /api/streaming→  YARP  →  Streaming Engine (HTTP)    │
  │      │                                                       │
  │      ├─ /health, /ready →  Health Checks (Redis, Kafka)      │
  │      │                                                       │
  │      └─ /swagger, /swagger/v1/swagger.json →  OpenAPI 3.1    │
  │                                                              │
  │  Background services (IHostedService):                       │
  │      ├─ KafkaTelemetryConsumer (processed-telemetry topic)   │
  │      ├─ KafkaAlertConsumer (alerts topic)                    │
  │      └─ OnlinePresenceSweeper (TTL-based housekeeping)       │
  │                                                              │
  │  Telemetry sinks: OTLP → Collector → Prometheus + Tempo      │
  └──────────────────────────────────────────────────────────────┘
            │                       │                    │
            ▼                       ▼                    ▼
        Redis HA              Kafka brokers       Ingress Gateway
      (Sentinel/Cluster)    (Phase 2 owns)       (Phase 1 owns)
```

---

## 1.5 Data flow (happy path: telemetry → dashboard update)

1. Truck → gRPC `Ingest` → **Ingress Gateway** (Phase 1).
2. Ingress → Kafka `fleet.telemetry.raw`.
3. **Streaming Engine** (Phase 2) consumes, enriches, scores, deduplicates → produces `fleet.telemetry.processed` + `fleet.alerts`.
4. **BFF API** (this service) consumes `fleet.telemetry.processed`:
   - Updates `truck:state:{truckId}` in Redis.
   - Updates `trucks:online` / `trucks:moving` sorted sets.
   - Invalidates `fleet:summary` cache.
   - Broadcasts to all SignalR clients in `fleet` group via `IFleetHubClient.OnTruckStateUpdate`.
5. Browser dashboard receives the SignalR message and patches the React store in < 100 ms.

Failure paths are specified in [04-data-model.md](04-data-model.md) and [05-security.md](05-security.md).

---

## 1.6 Cross-cutting decisions

| Concern              | Decision                                                                                  | Rationale                                              |
| -------------------- | ----------------------------------------------------------------------------------------- | ------------------------------------------------------ |
| AuthN                | JWT bearer (HS256 dev / RS256 prod), short-lived access tokens.                           | Standard, well-supported, dashboard-friendly.          |
| AuthZ                | Role claims (`fleet:reader`, `fleet:admin`, `alerts:ack`).                                | Simple, no per-row ACLs needed in Phase 3.             |
| API style            | REST + JSON for queries, SignalR for push, no GraphQL.                                    | GraphQL is overkill for this read shape.               |
| API versioning       | URL segment (`/api/v{version}/…`) + `Asp.Versioning.Mvc`.                                 | Cleaner than header-based for browser clients.         |
| OpenAPI              | `Swashbuckle.AspNetCore` 6.x with `Microsoft.AspNetCore.OpenApi` 10.x.                    | Generate `@phase4/dashboard` client via Orval.         |
| Validation           | FluentValidation 11.x via MediatR `ValidationBehavior`.                                   | Errors come back in a consistent envelope.             |
| Caching              | Redis 7 (Sentinel in dev, Cluster in prod) via `StackExchange.Redis` 2.8.x.              | Sentinel/Cluster both work; code is config-driven.    |
| SignalR scale-out    | `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.x backplane.                        | Lets us run N pods without sticky sessions.           |
| Rate limiting        | `Microsoft.AspNetCore.RateLimiting` 10.x, fixed-window 1000 req/min/IP.                   | Built-in, no extra dep.                                |
| Logging              | `Microsoft.Extensions.Logging` + JSON console sink.                                       | 12-factor; collector-agnostic.                         |
| Tracing              | OpenTelemetry 1.9.x → OTLP.                                                               | Vendor-neutral.                                        |
| Metrics              | OpenTelemetry `System.Diagnostics.Metrics` + Prometheus exporter.                         | Same pipeline as traces.                              |
| Configuration        | `appsettings.json` + env-var override (`__` separator).                                   | Standard ASP.NET Core pattern.                         |
| Resilience           | `Microsoft.Extensions.Resilience` (Polly v8) for HTTP retries on YARP.                    | Modern, no separate Polly package.                     |
| Time                 | `TimeProvider` (System) injected; never call `DateTime.UtcNow` directly in handlers.     | Determinism in tests.                                  |
| Serialization        | `System.Text.Json` source-generated contexts for hot DTOs.                                | Allocation-free on the summary hot path.               |

---

## 1.7 Package matrix (.NET 10)

> The Phase 3 spec targets **.NET 10**, the current major release. The package matrix below is the source of truth.

| Package                                                       | Version (10.x) | Why this version                                                      |
| ------------------------------------------------------------- | -------------- | --------------------------------------------------------------------- |
| `Microsoft.AspNetCore.App` (shared framework)                 | 10.0.x         | Current major; ships with the .NET 10 runtime.                        |
| `Microsoft.AspNetCore.Authentication.JwtBearer`               | 10.0.x         | Matches shared framework.                                             |
| `Microsoft.AspNetCore.SignalR.StackExchangeRedis`             | 10.0.x         | Matches shared framework.                                             |
| `Microsoft.AspNetCore.OpenApi`                                | 10.0.x         | Required by `AddOpenApi()`.                                           |
| `Swashbuckle.AspNetCore`                                      | 6.6.x          | Last 6.x; supports `Microsoft.OpenApi` 1.6.                           |
| `StackExchange.Redis`                                         | 2.8.x          | Current stable; works with both Sentinel and Cluster.                 |
| `Microsoft.Extensions.Caching.StackExchangeRedis`             | 10.0.x         | Matches shared framework.                                             |
| `Confluent.Kafka`                                             | 2.6.x          | Current stable on .NET 10; librdkafka 2.4.x bundled.                  |
| `MediatR`                                                     | 12.4.x         | Last v12 (free).                                                      |
| `FluentValidation`                                            | 11.10.x        | Current v11 release.                                                  |
| `FluentValidation.DependencyInjectionExtensions`              | 11.10.x        | Pairs with the above.                                                 |
| `YARP.ReverseProxy`                                           | 2.2.x          | Current 2.x release with .NET 10 support.                             |
| `Asp.Versioning.Mvc` / `Asp.Versioning.Mvc.ApiExplorer`       | 8.1.x          | Versioning + Swagger integration.                                     |
| `AspNetCore.HealthChecks.Redis`                               | 9.0.x          | Last release with explicit ASP.NET Core 8/9/10 support.               |
| `AspNetCore.HealthChecks.Kafka`                               | 9.0.x          | Last release with explicit ASP.NET Core 8/9/10 support.               |
| `OpenTelemetry.Extensions.Hosting`                            | 1.10.x         | OTEL hosting integration.                                             |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol`                | 1.10.x         | OTLP exporter.                                                        |
| `OpenTelemetry.Exporter.Prometheus.AspNetCore`                | 1.10.x         | `/metrics` endpoint.                                                  |
| `OpenTelemetry.Instrumentation.AspNetCore`                    | 1.10.x         | Auto-instrumentation.                                                 |
| `OpenTelemetry.Instrumentation.Http`                          | 1.10.x         | YARP + outbound HTTP tracing.                                         |
| `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` | 2.9.x / 17.12  | Test SDK.                                                             |
| `FluentAssertions`                                            | 7.x            | Test readability.                                                     |
| `NSubstitute`                                                 | 5.3.x          | Mocking (no Moq license ambiguity).                                   |
| `Testcontainers.Redis` / `Testcontainers.Kafka`               | 4.x            | Integration tests against real services.                              |
| `Verify`                                                      | 26.x           | Snapshot testing for OpenAPI & JSON.                                  |

### Why .NET 10?

- **Current major.** .NET 10 is the active release line; it ships with new framework defaults (`Microsoft.AspNetCore.OpenApi` 10.x, native OpenAPI, source-generated `JsonSerializerContext` improvements, etc.).
- **Tooling alignment.** The .NET 10 SDK (10.0.300) is the active toolchain; pinning to .NET 10 means no TFM juggling.
- **Ecosystem.** All packages in the matrix above have explicit .NET 10 support (either an explicit 10.0 TFM or a multi-target that includes `net10.0`).
- The existing `net10.0` skeleton files in `src/` align with this spec as-is.

---

## 1.8 Folder layout (target)

```
BffApi/
├── FleetStream.sln
├── README.md
├── docker/
│   ├── Dockerfile
│   └── docker-compose.yml
├── docs/                          ← you are here
│   ├── README.md
│   ├── 01-architecture.md
│   ├── 02-api-contract.md
│   ├── 03-signalr-protocol.md
│   ├── 04-data-model.md
│   ├── 05-security.md
│   ├── 06-configuration.md
│   ├── 07-observability.md
│   ├── 08-deployment.md
│   ├── 09-testing.md
│   └── 10-roadmap.md
├── src/
│   ├── Core/
│   │   ├── Common/                ← BaseEntity, ValueObject, Result<T>
│   │   ├── Domain/                ← Entities, ValueObjects, DomainEvents, Exceptions
│   │   └── FleetStream.Core.csproj (net10.0, no external deps)
│   ├── Application/
│   │   ├── Abstractions/          ← Interfaces (no impl)
│   │   ├── Behaviors/             ← MediatR pipeline behaviors
│   │   ├── Dtos/                  ← Wire DTOs
│   │   ├── Mappings/              ← Mapster / manual mappers
│   │   ├── Telemetry/             ← Use-cases (GetFleetSummary, GetTruckState, …)
│   │   ├── Validation/            ← FluentValidation rule sets
│   │   └── FleetStream.Application.csproj
│   ├── Infrastructure/
│   │   ├── Caching/               ← RedisCacheService
│   │   ├── Persistence/           ← InMemoryTruckRepository (dev) + EF Core adapter (prod, M5)
│   │   ├── RealTime/              ← RedisTruckStateStore, SignalRNotificationService
│   │   ├── Messaging/             ← KafkaTelemetryConsumer, KafkaAlertConsumer, JsonContext
│   │   ├── Time/                  ← SystemTimeProvider
│   │   └── FleetStream.Infrastructure.csproj
│   └── Presentation/
│       ├── Controllers/           ← FleetController, AuthController, HealthController
│       ├── Hubs/                  ← FleetHub, IFleetHubClient
│       ├── Middleware/            ← ExceptionHandlingMiddleware, CorrelationIdMiddleware
│       ├── Filters/               ← ValidationProblemFilter
│       ├── Auth/                  ← JwtBearer setup, dev-token issuer
│       ├── OpenApi/               ← Swagger configuration, OperationFilter, JWT scheme
│       ├── Program.cs             ← composition root
│       ├── appsettings.json
│       └── FleetStream.Presentation.csproj
└── tests/
    ├── FleetStream.UnitTests/             ← Core + Application (no I/O)
    ├── FleetStream.InfrastructureTests/   ← Redis + Kafka via Testcontainers
    ├── FleetStream.ApiTests/              ← WebApplicationFactory + contract tests
    └── FleetStream.LoadTests/             ← NBomber / k6 scripts
```

---

## 1.9 Open architectural questions

These are tracked in [10-roadmap.md](10-roadmap.md) §Decision log. They are deliberately *not* pre-decided here so reviewers can see the trade-offs:

1. **Redis Cluster vs. Sentinel in production.** Spec assumes Cluster; revisit if the prod target is single-AZ.
2. **EF Core adoption milestone.** Spec keeps the in-memory repo for Phase 3, EF Core adapter deferred to M5.
3. **YARP kept vs. removed.** If Phase 4 calls backend services directly, YARP becomes dead weight.
4. **Multi-tenancy.** Not in scope for Phase 3. If needed, will land in Phase 5.
5. **Per-truck authorization.** A viewer can only see trucks in their `region`. Defer unless the dashboard demands it.

---

## 1.10 Acceptance criteria for this document

This architecture spec is **final** when:

- [ ] All §1.6 decisions are reflected in `Program.cs` and the four `.csproj` files.
- [ ] The four project files compile against `net10.0` with the package matrix above.
- [ ] `dotnet test` runs the unit suite in < 30 s.
- [ ] `docker compose up` brings the whole stack (Redis, Kafka, BFF) to a healthy `/ready` state.
- [ ] `dotnet publish` produces a single self-contained or framework-dependent image that runs as non-root and serves the OpenAPI document at `/swagger/v1/swagger.json`.
