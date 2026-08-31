# BFF API — Production Readiness Checklist



> **Application:** `BffApi` (.NET 10)  

> **Audit date:** 2026-08-31  

> **Status:** P0/P1/P2 resolved — verification pending



Reference: [BffApi/docs](../BffApi/docs/README.md) · Architecture specs in `BffApi/docs/`



---



## P0 — Block production deployment



- [x] Wire RS256/JWKS production JWT validation (`Jwt:JwksUri`); remove symmetric-key-only path for Production

- [x] Conditionally register `AuthController` and `DevTokenIssuer` only in Development (avoid DI failure in Production)

- [x] Register Kafka health check on `/api/v1/health/ready` (`AspNetCore.HealthChecks.Kafka` is referenced but not wired)

- [x] Fix Dockerfile `HEALTHCHECK` path: use `/api/v1/health/live` instead of `/health`

- [x] Ensure `docker-compose` Production profile supplies JWT/JWKS config (no missing secrets at startup)



## P1 — Operational stability



### Observability



- [x] Configure JSON console logging with required fields: `traceId`, `spanId`, `correlationId`, `service`, `version`

- [x] Inject `correlationId` from `HttpContext.Items` into log scopes (handlers, middleware, Kafka consumers)

- [x] Add YARP request transform to forward `X-Correlation-Id` to ingress/streaming upstreams

- [x] Implement custom `fleetstream_bff_*` metrics per `07-observability.md` (Kafka, Redis, SignalR, cache)

- [x] Configure OTLP exporter endpoint for non-dev environments (`OpenTelemetry:OtlpEndpoint`)



### Resiliency



- [x] Add `Microsoft.Extensions.Resilience` (Polly v8) with timeouts and retries on YARP upstream clusters

- [x] Wire `ValidationDecorator` in `Program.cs` (validators exist but only `LoggingDecorator` is registered)

- [x] Apply rate limiter globally; honor `RateLimitOptions` from config; exempt health, swagger, SignalR negotiate



### Security



- [x] Require JWT on `FleetHub` (`/hubs/v1/fleet`)

- [x] Validate `JoinTruckGroup(truckId)` against regex and auth claims per `05-security.md`

- [x] Implement `SecurityHeadersMiddleware` (HSTS, `X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options`)

- [x] Implement audit logging category `FleetStream.Audit` for authenticated requests



## P2 — Maintainability & spec compliance



### API surface



- [x] Add `GET /api/v1/fleet/alerts` (active alerts list)

- [x] Add `GET /api/v1/fleet/trucks/{truckId}/telemetry` (telemetry history)

- [x] Implement cursor-based pagination per `02-api-contract.md`



### Architecture & deployment



- [x] Move `FleetHub` / `IFleetHubClient` from Infrastructure to `Presentation/Hubs/`

- [x] Implement `Features:DevToken` via feature flags (optional; currently env-check only)

- [x] Expand root `docker-compose` to include Go services for full-stack E2E

- [x] Add `FleetStream.InfrastructureTests` and `FleetStream.ApiTests` per `09-testing.md`

- [x] Commit Grafana dashboard JSON under `ops/grafana/bff-overview.json`



### Configuration



- [x] Add `appsettings.Production.json` with safe defaults (no secrets)

- [x] Log effective configuration (redacted) on startup per `06-configuration.md` §6.8



---



## Verification

> Build/test items are automated in CI (`.github/workflows/bff-api.yml`) and `scripts/verify-production-readiness.sh`. Runtime items require `--runtime` or manual docker-compose validation.

- [x] `dotnet build BffApi/FleetStream.sln` — 0 errors (CI)

- [ ] `GET /api/v1/health/live` → 200

- [ ] `GET /api/v1/health/ready` → 200 when Redis + Kafka up; 503 when either down

- [ ] `GET /metrics` exposes custom `fleetstream_bff_*` metrics

- [ ] Unauthenticated `GET /api/v1/fleet/summary` → 401

- [ ] `POST /api/v1/auth/dev-token` → 404 outside Development

- [ ] Sample trace for `GET /api/v1/fleet/summary` includes HTTP + Redis spans with shared `correlationId`

