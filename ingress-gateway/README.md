# Phase 1: Ingress Gateway (Go Engineering)

## Overview

High-throughput telemetry ingestion service built in Go. Accepts telemetry from thousands of trucks concurrently, normalizes payloads, and uses a sharded worker pool to publish events to Kafka with backpressure handling.

## Key Features

- **Multi-Protocol Support**: gRPC, WebSocket, HTTP REST
- **Sharded Worker Pool**: 8 shards × 4 workers = 32 concurrent processors
- **Kafka Producer**: Async, compressed, with drop-on-full backpressure
- **Prometheus Metrics**: Real-time monitoring at `/metrics`

## Technical Specifications

| Metric | Target |
|--------|--------|
| Concurrent Connections | 10,000+ |
| Processing Latency | < 1ms |
| Throughput | 100K+ msg/s |
| Memory Usage | < 512MB |

## Quick Start

```bash
# Build
go build -o ingress-gateway ./cmd/server

# Run
./ingress-gateway
```

## API Endpoints

- **HTTP POST**: `POST http://localhost:8080/ingest`
- **WebSocket**: `ws://localhost:8080/ws`
- **gRPC**: `localhost:50051`
- **Liveness**: `GET http://localhost:8080/health/live`
- **Readiness**: `GET http://localhost:8080/health/ready` (Kafka reachable + pool accepting)
- **Metrics**: `http://localhost:9090/metrics`

## Load Testing

```bash
# Throughput (10K simulated trucks)
go run cmd/loadtest/main.go --mode=throughput --trucks=10000 --rate=10

# Concurrent connections gate (10K WebSocket connections)
go run cmd/loadtest/main.go --mode=connections --connections=10000 --duration=30s --gate

# Or via Makefile (requires running ingress-gateway)
make loadtest-gate
```

## Integration Tests

```bash
# Requires Kafka at KAFKA_BROKERS (default localhost:9092)
make integration
```

## Configuration

Config loads in order: defaults → optional YAML (`CONFIG_FILE`) → environment variables (highest precedence).

| Variable | Maps to | Default |
|---|---|---|
| `CONFIG_FILE` | YAML overlay path | unset |
| `GRPC_PORT` | `Server.GRPCPort` | `50051` |
| `HTTP_PORT` | HTTP/WebSocket listen port | `8080` |
| `WEBSOCKET_PORT` | `Server.WebsocketPort` (overrides `HTTP_PORT`) | `8080` |
| `READ_TIMEOUT` | `Server.ReadTimeout` | `5s` |
| `WRITE_TIMEOUT` | `Server.WriteTimeout` | `10s` |
| `IDLE_TIMEOUT` | `Server.IdleTimeout` | `60s` |
| `KAFKA_BROKERS` | `Kafka.Brokers` (comma-separated) | `localhost:9092` |
| `KAFKA_TOPIC` | `Kafka.Topic` | `fleet.telemetry.raw` |
| `KAFKA_CLIENT_ID` | `Kafka.ClientID` | `ingress-gateway` |
| `KAFKA_COMPRESSION` | `Kafka.Compression` | `snappy` |
| `WORKER_POOL_SHARDS` | `WorkerPool.Shards` | `8` |
| `WORKER_POOL_WORKERS_PER_SHARD` | `WorkerPool.WorkersPerShard` | `4` |
| `WORKER_POOL_QUEUE_SIZE` | `WorkerPool.QueueSize` | `10000` |
| `BACKPRESSURE_ENABLED` | `Backpressure.Enabled` | `true` |
| `BACKPRESSURE_DROP_ON_FULL` | `Backpressure.DropOnFull` | `true` |
| `BACKPRESSURE_MAX_QUEUE_DEPTH` | `Backpressure.MaxQueueDepth` | `100000` |
| `METRICS_ENABLED` | `Metrics.Enabled` | `true` |
| `METRICS_PORT` | `Metrics.Port` | `9090` |
| `LOG_LEVEL` | `Logging.Level` | `info` |
| `LOG_FORMAT` | `Logging.Format` (`json` or `text`) | `json` |
| `LOG_OUTPUT` | `Logging.Output` (`stdout`, `stderr`, `file`) | `stdout` |
| `SHUTDOWN_TIMEOUT` | `Shutdown.Timeout` | `30s` |

```yaml
worker_pool:
  shards: 8
  workers_per_shard: 4
  queue_size: 10000

backpressure:
  enabled: true
  drop_on_full: true
  max_queue_depth: 100000
```

## Resume Claims Validated

✅ High-throughput signal fan-out with Go concurrency
✅ Sharded worker pool with consistent hashing
✅ Drop-on-full backpressure semantics
✅ 10,000+ concurrent connections support
✅ Sub-millisecond processing latency
✅ Kafka ETL with compression and batching

## Next Steps

- Phase 2: Streaming Engine (Kafka consumers, exactly-once processing)
- Phase 3: BFF API (.NET 8 Clean Architecture)
- Phase 4: Dashboard (Next.js real-time UI)
