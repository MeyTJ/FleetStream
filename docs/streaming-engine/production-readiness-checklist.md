# Streaming Engine — Production Readiness Checklist

> **Application:** `streaming-engine` (Go)  
> **Audit date:** 2026-08-31  
> **Status:** P0/P1/P2 resolved — verification remaining

Reference: [streaming-engine/README.md](../../streaming-engine/README.md)

---

## P0 — Block production deployment

### Build & reproducibility

- [x] Generate and commit `go.sum` (required by Dockerfile; builds currently fail)
- [x] Fix compile error in `internal/dlq/handler.go`: add `RetryBackoff` to `DLQConfig` or remove references
- [x] Verify `go build ./cmd/processor` succeeds in CI

### Message processing correctness

- [x] Do **not** `MarkMessage` / commit offset when handler returns an error
- [x] Invoke `dlqHandler.SendToDLQ(ctx, msg, err)` on processing failures (handler is created but unused)
- [x] Only commit offset after successful process + state update + publish
- [x] Verify exactly-once semantics under failure scenarios (Redis down, publish failure)

### Configuration (12-factor)

- [x] Load config from environment variables and/or config file — stop using `DefaultConfig()` only in `main`
- [x] Wire all `pkg/config/config.go` sections (Consumer, Producer, Redis, Processing, Idempotency, DLQ, Metrics, Shutdown)
- [x] Document env var mapping (e.g. `KAFKA_BROKERS`, `REDIS_ADDRESSES`, `CONSUMER_TOPIC`)

## P1 — Operational stability

### Health endpoints

- [x] Split liveness and readiness:
  - [x] `GET /health/live` — process is up
  - [x] `GET /health/ready` — Kafka consumer connected, Redis ping OK, circuit breakers not open
- [x] Move health server off port `:9092` (conflicts with Kafka default broker port)
- [x] Use dedicated admin port (e.g. `:9092` → `:8081` or match `Metrics.Port + 1`)
- [x] Fix Dockerfile health check to match final port/path

### Graceful shutdown

- [x] Track HTTP servers (metrics, health) and call `Shutdown(ctx)` on signal
- [x] Enforce `ShutdownConfig.Timeout` during drain (consumer close, producer flush, Redis close)
- [x] Wait for in-flight message processing to complete before exit
- [x] Close DLQ producer on shutdown

### Redis client

- [x] Use `redis.NewClient` for single-node dev; `redis.NewClusterClient` only when cluster addresses provided
- [x] Make Redis topology config-driven (single vs cluster)

### Observability

- [x] Add middleware or handler wrapper for `X-Correlation-Id` on admin HTTP endpoints
- [x] Include correlation/trace ID in Kafka produced message headers
- [x] Propagate context through `state.SetTruckState`, `producer.Input()`, and DLQ publish

## P2 — Maintainability & spec compliance

- [x] Wire DLQ retry logic (`processRetries`) to actual reprocessing or remove dead code
- [x] Externalize log level via config
- [x] Add consumer lag gauge (`kafka_consumer_lag`) for Prometheus
- [x] Include streaming-engine in root `docker-compose` for full-stack E2E
- [x] Add integration test: consume from `fleet.telemetry.raw` → produce to `fleet.telemetry.processed`
- [x] Validate anomaly detection thresholds are loaded from config, not hardcoded in processor

---

## Verification

> Build/test items are automated in CI (`.github/workflows/streaming-engine.yml`) and `scripts/verify-production-readiness.sh`. Runtime items require `--runtime` or manual docker-compose validation.

- [x] `go build -o streaming-engine ./cmd/processor` — succeeds (CI)
- [x] `docker build` succeeds with committed `go.sum` (CI docker job)
- [ ] Message on `fleet.telemetry.raw` → processed output on `fleet.telemetry.processed`
- [ ] Duplicate `MessageID` dropped when idempotency enabled
- [ ] Processing failure → message in DLQ topic; offset **not** committed
- [ ] `GET /health/live` → 200
- [ ] `GET /health/ready` → 200 when Kafka + Redis up; 503 when either down
- [ ] `GET :9091/metrics` → Prometheus format with `fleetstream_streaming_*` metrics
- [ ] SIGTERM → consumer closes cleanly within `Shutdown.Timeout`
- [ ] Logs are JSON; failed messages include topic, partition, offset, error
