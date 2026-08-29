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

## Configuration

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

- **Health**: `http://localhost:9092/health`
- **Metrics**: `http://localhost:9091/metrics`

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
