# 07 — Observability

> **Status:** 🟡 Draft
> **Audience:** SRE, on-call, engineers
> **Goal:** Define the logging, metrics, tracing, health-check, SLO, and alerting posture of the BFF API.

---

## 7.1 Three pillars

| Pillar    | Mechanism                              | Sink                                     |
| --------- | -------------------------------------- | ---------------------------------------- |
| Logs      | `Microsoft.Extensions.Logging` + JSON  | stdout (collected by the platform)       |
| Metrics   | OpenTelemetry `System.Diagnostics.Metrics` + Prometheus exporter | `/metrics` scraped by Prometheus |
| Traces    | OpenTelemetry → OTLP                   | OTLP collector → Tempo (or vendor)       |

All three carry the same `correlationId` and `traceId` so an SRE can pivot between them.

---

## 7.2 Logging

- **Format:** one JSON object per line, UTF-8, no embedded newlines.
- **Minimum fields on every line:** `timestamp`, `level`, `category`, `message`, `traceId`, `spanId`, `correlationId`, `service`, `version`.
- **Levels:**
  - `Trace`/`Debug` — disabled in Production.
  - `Information` — default; "request handled", "telemetry updated", "alert ack'd".
  - `Warning` — degraded behavior (cache miss + Redis down, backpressure, etc.).
  - `Error` — unhandled exceptions, DLQ writes, JWT validation failures.
  - `Critical` — process is broken; will likely crash.
- **PII discipline:** never log raw JWTs, license plates, or `metadata.Metadata` from alerts. License plates are masked to the first 3 chars (`TAC-***-***`).
- **Sink:** console JSON, no file sinks.

```json
{
  "timestamp": "2026-08-29T12:34:56.789Z",
  "level":     "Information",
  "category":  "FleetStream.Application.FleetQueryService",
  "message":   "Computed fleet summary: online=9871 moving=7220",
  "traceId":   "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "spanId":    "00f067aa0ba902b7",
  "correlationId": "c-1f3a",
  "service":   "fleetstream-bff",
  "version":   "1.0.0"
}
```

---

## 7.3 Metrics

The BFF emits the following metrics, all under the `fleetstream_bff_*` namespace. They are defined in `Metrics/BffMetrics.cs` and registered with `System.Diagnostics.Metrics`.

### Counters

| Metric                                              | Labels                  | Source                            |
| --------------------------------------------------- | ----------------------- | --------------------------------- |
| `fleetstream_bff_http_requests_total`               | `method,route,status`   | `UseOpenTelemetry().WithMetrics()` |
| `fleetstream_bff_http_request_duration_seconds`     | `method,route`          | (histogram)                       |
| `fleetstream_bff_kafka_messages_total`              | `topic,result`          | `KafkaTelemetryConsumer`          |
| `fleetstream_bff_kafka_consumer_errors_total`       | `topic,kind`            | `KafkaTelemetryConsumer`          |
| `fleetstream_bff_redis_operations_total`            | `op,result`             | `RedisTruckStateStore`            |
| `fleetstream_bff_signalr_messages_total`            | `direction,method`      | `FleetHub`                        |
| `fleetstream_bff_signalr_connections_active`        | —                       | gauge from `IHubContext`          |
| `fleetstream_bff_signalr_messages_dropped_total`    | `reason`                | `FleetHub`                        |
| `fleetstream_bff_alerts_acknowledged_total`         | `severity`              | `FleetController`                 |
| `fleetstream_bff_cache_hits_total`                  | `key_pattern`           | `RedisCacheService`               |
| `fleetstream_bff_cache_misses_total`                | `key_pattern`           | `RedisCacheService`               |

### Gauges

- `fleetstream_bff_signalr_connections_active` — updated every 5 s.
- `fleetstream_bff_kafka_consumer_lag` — pulled from the consumer's `QueryWatermarkOffsets`.

### Histograms

- `fleetstream_bff_http_request_duration_seconds` — buckets: 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10.
- `fleetstream_bff_kafka_processing_duration_seconds` — same buckets.

### Exemplars

- Every histogram point carries the active traceId when one is active (automatic via OTel).

---

## 7.4 Tracing

- **Library:** `OpenTelemetry.Extensions.Hosting` 1.9.x, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`.
- **Exporter:** OTLP/gRPC to `${OpenTelemetry:OtlpEndpoint}`.
- **Service identification:** `service.name=fleetstream-bff`, `service.version=<assembly version>`.
- **Sampling:**
  - **Always-on** in Development.
  - **Parent-based(TraceIdRatioBased(0.10))** in Production — 10 % of root traces, 100 % of children of sampled traces.
- **Spans:**
  - `HTTP <verb> <route>` (ASP.NET Core instrumentation).
  - `Kafka.Consume <topic>` (manual).
  - `Redis.<op> <key>` (manual via `ActivitySource`).
  - `SignalR.HubMethod <method>` (manual).
- **Span attributes** include `truck.id`, `truck.count`, `region`, and the standard `http.*` / `messaging.*` semconv attributes.

---

## 7.5 Health checks

| Endpoint                | Predicate                  | Checks                                                | Used by            |
| ----------------------- | -------------------------- | ----------------------------------------------------- | ------------------ |
| `GET /api/v1/health/live`  | always                     | process is up                                         | Kubernetes liveness |
| `GET /api/v1/health/ready` | tags = `["ready"]`         | Redis ping, Kafka producer metadata, own internal state | Kubernetes readiness |
| `GET /api/v1/health/startup` | tags = `["startup"]`    | (Optional) migrations, warm-ups                        | Kubernetes startup  |

- **Failure mode:** a 503 from `/ready` removes the pod from the load-balancer rotation.
- **Self-test:** the readiness check runs every 10 s and caches the result for 5 s to avoid hammering Redis on a flap.

---

## 7.6 SLOs

| SLI                                                | Target  | Window  | Error budget (30 d) |
| -------------------------------------------------- | ------- | ------- | ------------------ |
| Availability (2xx ratio, all `/api/*`)             | 99.9 %  | 30 d    | 43.2 min           |
| `/api/v1/fleet/summary` latency p99                | < 300 ms | 30 d   | n/a                |
| SignalR broadcast p99 (server→client)              | < 100 ms | 30 d   | n/a                |
| Kafka consumer lag p99                             | < 5 s   | 30 d    | n/a                |

**Burn-rate alerts:**
- Page when 2 % of the 30-day budget is consumed in 1 h (1 h burn-rate ≥ 14.4×).
- Page when 5 % is consumed in 6 h (6 h burn-rate ≥ 6×).

---

## 7.7 Dashboards (Grafana)

- **BFF overview** — RPS, p50/p95/p99 latency, error rate, by route.
- **Redis** — ops/sec by op, hit/miss ratio, p99 latency, connection pool.
- **Kafka** — messages/sec by topic, consumer lag, DLQ count.
- **SignalR** — active connections, messages/sec by direction, backpressure events.

All dashboards use the `service.name=fleetstream-bff` label and the `correlationId` link to Tempo for trace drill-down.

---

## 7.8 Alerting (Prometheus → Alertmanager)

| Alert                              | Condition                                                          | Severity | Runbook                                  |
| ---------------------------------- | ------------------------------------------------------------------ | -------- | ---------------------------------------- |
| `BffHighErrorRate`                 | `rate(errors[5m]) / rate(requests[5m]) > 0.05`                     | page     | `runbooks/bff-high-error-rate.md`        |
| `BffHighLatencyP99`                | `histogram_quantile(0.99, …) > 0.5` for 10 m                       | page     | `runbooks/bff-high-latency.md`           |
| `BffKafkaConsumerLag`              | `kafka_consumer_lag > 300` for 5 m                                  | page     | `runbooks/bff-kafka-lag.md`              |
| `BffDlqGrowing`                    | `increase(dlq[15m]) > 0`                                           | ticket   | `runbooks/bff-dlq.md`                    |
| `BffRedisDown`                     | `redis_up == 0` for 1 m                                            | page     | `runbooks/bff-redis-down.md`             |
| `BffSignalRConnectionsHigh`        | `signalr_connections_active > 9000` (warning at 80 % of limit)      | ticket   | `runbooks/bff-signalr-scale.md`          |
| `BffPodCrashLooping`               | `rate(kube_pod_container_status_restarts_total[15m]) > 0`          | page     | `runbooks/bff-pod-crash.md`              |

---

## 7.9 Acceptance criteria for this document

- [ ] Every log line is valid JSON and parses with `jq`.
- [ ] `/metrics` exposes all metrics in §7.3 in the Prometheus text format.
- [ ] A sample trace from `GET /api/v1/fleet/summary` includes spans for HTTP, MediatR, and Redis.
- [ ] A simulated Redis outage flips `/ready` to 503 within 10 s and back to 200 within 10 s of recovery.
- [ ] The Grafana dashboard JSON is committed under `ops/grafana/bff-overview.json` and importable.
