# FleetStream BFF API (Phase 3)

The **Backend-for-Frontend (BFF) API** for the FleetStream IoT Fleet Telemetry Platform, built with **.NET 10** following **Clean Architecture** principles.

## 🎯 Purpose

This BFF API acts as the **central nervous system** for fleet management:
- **Real-time state aggregation** from 10,000+ trucks via Redis HA
- **SignalR streaming** for sub-100ms dashboard updates
- **REST API** for fleet operations and management
- **YARP reverse proxy** for backend service composition
- **OpenAPI/Swagger** documentation for client codegen

## 🏗️ Clean Architecture

```
┌─────────────────────────────────────────────────────────┐
│                  Presentation Layer                      │
│  Controllers, Hubs (SignalR), Middleware, Program.cs   │
└────────────────────┬────────────────────────────────────┘
                     ↓
┌────────────────────▼────────────────────────────────────┐
│                  Application Layer                       │
│  Services (FleetQueryService), DTOs, Interfaces        │
└────────────────────┬────────────────────────────────────┘
                     ↓
┌────────────────────▼────────────────────────────────────┐
│                    Core Domain                           │
│  Entities, Value Objects, Domain Logic                  │
└────────────────────▲────────────────────────────────────┘
                     ↑
┌────────────────────┴────────────────────────────────────┐
│                Infrastructure Layer                      │
│  Redis Cache, SignalR, Kafka Consumer, Repositories     │
└─────────────────────────────────────────────────────────┘
```

## 📁 Project Structure

```
BffApi/
├── FleetStream.sln
├── src/
│   ├── Core/
│   │   ├── Domain/Entities/      # Truck, TruckState, Alert
│   │   └── Common/               # BaseEntity, ValueObject
│   ├── Application/
│   │   ├── Interfaces/           # ITruckRepository, ICacheService
│   │   ├── Services/             # FleetQueryService
│   │   └── Behaviors/            # MediatR pipeline
│   ├── Infrastructure/
│   │   ├── Caching/              # RedisCacheService
│   │   ├── Services/             # RedisTruckStateStore, SignalR
│   │   └── Data/                 # Repositories
│   └── Presentation/
│       ├── Controllers/          # FleetController
│       ├── Hubs/                  # SignalR FleetHub
│       ├── Program.cs             # Startup & DI
│       └── appsettings.json       # Configuration
└── docker/
    ├── Dockerfile
    └── docker-compose.yml
```

## 🚀 Key Features

### 1. High-Performance Caching (Redis HA)
- `RedisCacheService` with sub-millisecond response times
- `RedisTruckStateStore` maintains "order book"-style state
- Connection pooling and graceful error handling

### 2. Real-Time Streaming (SignalR + Redis Backplane)
- `FleetHub` broadcasts to 10,000+ connected clients
- Redis backplane enables horizontal scaling
- Group-based subscriptions (fleet-wide or per-truck)

### 3. REST API (ASP.NET Core)
- `FleetController` with OpenAPI/Swagger documentation
- Action result pattern with proper status codes
- Built-in model validation
- Rate limiting (1000 req/min per client)

### 4. YARP Reverse Proxy (BFF Composition)
- Aggregates calls to Ingress Gateway and Streaming Engine
- Dynamic configuration via `appsettings.json`

### 5. Observability
- Structured logging (ILogger)
- Health checks (Redis, Kafka)
- API versioning

## 📡 API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/fleet/summary` | Fleet-wide summary statistics |
| GET | `/api/fleet/trucks` | List all truck states (paginated) |
| GET | `/api/fleet/trucks/{id}` | Get truck details |
| GET | `/api/fleet/trucks/{id}/state` | Get current truck state |
| GET | `/swagger` | OpenAPI documentation |
| WS | `/hubs/fleet` | SignalR WebSocket |
| GET | `/health` | Liveness check |
| GET | `/ready` | Readiness check |

## 🏃 Running Locally

```bash
# With Docker Compose
cd docker
docker-compose up -d

# Or with .NET CLI
cd src/Presentation
dotnet run
```

Access Swagger UI: `http://localhost:8080/swagger`

## 📚 Specification Documents

Professional specifications for Phase 3 live under [`docs/`](docs/README.md). They are the source of truth for architecture, contracts, security, and operations:

| #   | Document                                  | Scope                                                  |
| --- | ----------------------------------------- | ------------------------------------------------------ |
| 01  | [Architecture](docs/01-architecture.md)   | Layers, runtime topology, data flow, .NET 10 package matrix. |
| 02  | [API Contract](docs/02-api-contract.md)   | REST surface, OpenAPI schemas, error model, versioning. |
| 03  | [SignalR Protocol](docs/03-signalr-protocol.md) | Hub methods, groups, payloads, reconnect behavior. |
| 04  | [Data Model](docs/04-data-model.md)       | Entities, DTOs, Redis keyspace, Kafka topic contracts. |
| 05  | [Security](docs/05-security.md)           | JWT auth, CORS, rate limiting, threat model, audit.    |
| 06  | [Configuration](docs/06-configuration.md) | `appsettings.json` schema, env-var overrides, feature flags. |
| 07  | [Observability](docs/07-observability.md) | Logging, metrics, tracing, health checks, SLOs, alerts. |
| 08  | [Deployment](docs/08-deployment.md)       | Docker image, compose stack, Kubernetes manifests, DR.  |
| 09  | [Testing](docs/09-testing.md)             | Test pyramid, xUnit + Testcontainers + k6, coverage.    |
| 10  | [Roadmap](docs/10-roadmap.md)             | Milestones, exit criteria, risks, decision log.         |

See the [docs index](docs/README.md) for the full document map.
