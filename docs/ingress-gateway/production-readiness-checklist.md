# Ingress Gateway — Production Readiness Checklist

> **Application:** `ingress-gateway` (Go)  
> **Audit date:** 2026-08-31  
> **Status:** P0/P1/P2 resolved

Reference: [ingress-gateway/README.md](../../ingress-gateway/README.md)

---

## P0 — Block production deployment

### Build & reproducibility

- [x] Generate and commit `go.sum` (required by Dockerfile; builds currently fail)
- [x] Fix compile errors in `internal/processors/sharded_pool.go`:
  - [x] Add `sync/atomic` import for `atomic.Bool` / `atomic.Uint64`
  - [x] Fix `processJob` — remove reference to non-existent `shard.processed` field
- [x] Verify `go build ./cmd/server` succeeds in CI

### Core ingestion path

- [x] Wire `KafkaProducer` into worker pool `processJob` (currently a stub that only increments a counter)
- [x] Integrate `telemetry_processor.go` normalization before Kafka publish
- [x] Implement gRPC server in `startGRPCServer` (currently logs "would start here" only)
- [x] Remove or gate stub code paths before production deploy

### Configuration (12-factor)

- [x] Load config from environment variables and/or config file — stop hardcoding in `cmd/server/main.go`
- [x] Wire `pkg/config/config.go` structs into `main` (Server, Kafka, WorkerPool, Backpressure, Metrics, Shutdown)
- [x] Document env var mapping (e.g. `KAFKA_BROKERS`, `GRPC_PORT`, `HTTP_PORT`)

## P1 — Operational stability

### Health endpoints

- [x] Split liveness and readiness:
  - [x] `GET /health/live` — process is up
  - [x] `GET /health/ready` — Kafka broker reachable, worker pool accepting jobs
- [x] Fix Dockerfile `HEALTHCHECK`: metrics server exposes `/metrics` on `:9090`, not `/health`
- [x] Align health check port/path with actual server layout

### Graceful shutdown

- [x] Track `*http.Server` instances (HTTP/WS, metrics) in `main`
- [x] On `SIGINT`/`SIGTERM`, call `server.Shutdown(ctx)` with timeout from `ShutdownConfig`
- [x] Pass non-cancelled shutdown context to `pool.Shutdown()` (currently uses already-cancelled ctx)
- [x] Close Kafka producer cleanly on shutdown
- [x] Stop gRPC server gracefully when implemented

### HTTP server hardening

- [x] Set `ReadTimeout`, `WriteTimeout`, `IdleTimeout` on `http.Server` from `ServerConfig`
- [x] Stop using global `http.DefaultServeMux`; use dedicated muxes per server to avoid route collisions

### Observability

- [x] Add middleware to read/generate `X-Correlation-Id` and include in all log entries
- [x] Propagate correlation ID into Kafka message headers on publish
- [x] Accept W3C `traceparent` header and attach to logs (optional: OpenTelemetry Go SDK)

## P2 — Maintainability & spec compliance

- [x] Externalize log level via config (`LoggingConfig.Level`)
- [x] Add readiness probe that reflects worker pool queue depth / backpressure state
- [x] Include ingress-gateway in root `docker-compose` for full-stack local validation
- [x] Add integration test: ingest via HTTP → message appears on `fleet.telemetry.raw`
- [x] Load test gate: validate 10K concurrent connections claim with `cmd/loadtest`

---

## Verification

> Build/test items are automated in CI (`.github/workflows/ingress-gateway.yml`) and `scripts/verify-production-readiness.sh`. Runtime items require `--runtime` or manual docker-compose validation.

- [x] `go build -o ingress-gateway ./cmd/server` — succeeds (CI)
- [x] `docker build` succeeds with committed `go.sum` (CI docker job)
- [ ] `POST /ingest` with valid payload → message on Kafka topic `fleet.telemetry.raw`
- [ ] `GET /health/live` → 200
- [ ] `GET /health/ready` → 200 when Kafka up; 503 when Kafka down
- [ ] `GET :9090/metrics` → Prometheus format
- [ ] SIGTERM → in-flight requests drain within `Shutdown.Timeout`; no message loss beyond documented drop-on-full policy
- [ ] Logs are JSON with `correlationId` on each ingest request
