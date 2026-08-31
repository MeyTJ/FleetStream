# Phase 2: Streaming Engine (Data Processing & Kafka)

## Overview

The Streaming Engine is the central nervous system of FleetStream. It consumes raw telemetry from Kafka, processes it with exactly-once semantics, enriches with geographic and risk data, and republishes to downstream consumers.

## Key Features

- **Exactly-Once Processing**: Idempotent consumer + manual offset management + idempotent producer
- **Stream Processing**: Enrichment, anomaly detection, risk scoring
- **State Management**: Redis cluster with atomic Lua-script updates
- **Resilience**: Circuit breakers, DLQ, retry mechanism
- **Monitoring**: Prometheus metrics, health checks

## Technical Specifications

| Metric | Target |
|--------|--------|
| Throughput | 100K+ msg/s |
| Latency | < 5ms |
| Deduplication | 100% (Redis SETNX) |
| State Lookups | < 1ms |

## Architecture

```
Kafka: fleet.telemetry.raw
        │
        ▼
   [Consumer Group - Exactly-Once]
        │
        ▼
   [Idempotency Check - Redis]
        │
        ▼
   [Stream Processor]
   - Geographic Enrichment
   - Anomaly Detection
   - Risk Scoring
        │
        ▼
   [State Update - Redis]
        │
        ▼
   [Idempotent Producer]
        │
        ▼
Kafka: fleet.telemetry.processed
```

## Project Structure

```
streaming-engine/
├── cmd/processor/main.go       # Main entry point
├── internal/
│   ├── consumer/               # Kafka consumer
│   ├── processor/              # Stream processing
│   ├── state/                  # Redis state store
│   ├── metrics/                # Prometheus metrics
│   ├── dlq/                    # Dead letter queue
│   ├── circuit/                # Circuit breakers
│   └── enrichment/             # Geographic enrichment
├── pkg/
│   ├── config/                 # Configuration
│   └── models/                 # Data models
├── Dockerfile
└── README.md
```

## Quick Start

```bash
go build -o streaming-engine ./cmd/processor
./streaming-engine
```

Integration tests (requires Kafka + Redis, e.g. `docker compose up -d kafka redis`):

```bash
make integration
```

## Configuration

Config loads in order: defaults → optional YAML (`CONFIG_FILE`) → environment variables (highest precedence).

| Variable | Maps to | Default |
|---|---|---|
| `CONFIG_FILE` | YAML overlay path | unset |
| `KAFKA_BROKERS` | Consumer, Producer, and DLQ brokers (comma-separated) | `localhost:9092` |
| `CONSUMER_BROKERS` | `Consumer.Brokers` (overrides `KAFKA_BROKERS`) | `localhost:9092` |
| `CONSUMER_TOPIC` | `Consumer.Topic` | `fleet.telemetry.raw` |
| `CONSUMER_GROUP_ID` | `Consumer.GroupID` | `fleetstream-streaming-processor` |
| `CONSUMER_START_OFFSET` | `Consumer.StartOffset` (`earliest` / `newest`) | `earliest` |
| `PRODUCER_BROKERS` | `Producer.Brokers` (overrides `KAFKA_BROKERS`) | `localhost:9092` |
| `PRODUCER_TOPIC` | `Producer.Topic` | `fleet.telemetry.processed` |
| `PRODUCER_COMPRESSION` | `Producer.Compression` | `snappy` |
| `PRODUCER_IDEMPOTENT` | `Producer.Idempotent` | `true` |
| `REDIS_ADDRESSES` | `Redis.Addresses` (comma-separated) | `localhost:6379` |
| `REDIS_ADDR` | alias for `REDIS_ADDRESSES` | `localhost:6379` |
| `REDIS_PASSWORD` | `Redis.Password` | empty |
| `REDIS_DB` | `Redis.DB` | `0` |
| `REDIS_CLUSTER` | `Redis.Cluster` (auto-enabled when multiple addresses) | `false` |
| `IDEMPOTENCY_ENABLED` | `Idempotency.Enabled` | `true` |
| `IDEMPOTENCY_DROP_DUPLICATES` | `Idempotency.DropDuplicates` | `true` |
| `DLQ_ENABLED` | `DLQ.Enabled` | `true` |
| `DLQ_TOPIC` | `DLQ.Topic` | `fleet.telemetry.dlq` |
| `DLQ_BROKERS` | `DLQ.Brokers` (overrides `KAFKA_BROKERS`) | `localhost:9092` |
| `DLQ_RETRY_ATTEMPTS` | `DLQ.RetryAttempts` | `3` |
| `DLQ_RETRY_BACKOFF` | `DLQ.RetryBackoff` | `5s` |
| `METRICS_ENABLED` | `Metrics.Enabled` | `true` |
| `METRICS_PORT` | `Metrics.Port` | `9091` |
| `METRICS_PATH` | `Metrics.Path` | `/metrics` |
| `ADMIN_PORT` | `Admin.Port` | `8081` |
| `LOG_LEVEL` | `Logging.Level` (`trace`, `debug`, `info`, `warn`, `error`) | `info` |
| `SHUTDOWN_TIMEOUT` | `Shutdown.Timeout` | `60s` |
| `ANOMALY_MAX_SPEED_KMH` | `Processing.AnomalyThreshold.MaxSpeedKmh` | `120` |
| `ANOMALY_MAX_ENGINE_TEMP_CELSIUS` | `Processing.AnomalyThreshold.MaxEngineTempCelsius` | `110` |

```yaml
consumer:
  topic: fleet.telemetry.raw
  group_id: fleetstream-streaming-processor
  enable_auto_commit: false

producer:
  topic: fleet.telemetry.processed
  compression: snappy
  idempotent: true

redis:
  addresses: [localhost:6379]
  pool_size: 100
  state_ttl: 24h

processing:
  concurrency: 8
  anomaly_threshold:
    max_speed_kmh: 120
    max_engine_temp_celsius: 110
```

## API Endpoints

- **Liveness**: `http://localhost:8081/health/live`
- **Readiness**: `http://localhost:8081/health/ready`
- **Metrics**: `http://localhost:9091/metrics`

Admin endpoints accept and return `X-Correlation-Id`. Produced Kafka messages include correlation headers propagated from consumed messages.

## Resume Claims Validated

✅ Kafka ETL with exactly-once semantics
✅ Idempotent producer + manual offset commit
✅ 100K-message backpressure handling
✅ Redis-based deduplication
✅ Stream processing with enrichment
✅ Anomaly detection (speed, temp, fuel)
✅ Circuit breaker pattern
✅ Dead letter queue

## Next Steps

- Phase 3: BFF API (.NET 8 Clean Architecture)
- Phase 4: Dashboard (Next.js)
