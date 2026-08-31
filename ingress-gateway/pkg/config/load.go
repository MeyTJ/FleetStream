package config

import (
	"fmt"
	"os"
	"strconv"
	"strings"
	"time"

	"gopkg.in/yaml.v3"
)

func Load() (*Config, error) {
	cfg := DefaultConfig()
	if path := os.Getenv("CONFIG_FILE"); path != "" {
		data, err := os.ReadFile(path)
		if err != nil {
			return nil, fmt.Errorf("read CONFIG_FILE: %w", err)
		}
		if err := yaml.Unmarshal(data, cfg); err != nil {
			return nil, fmt.Errorf("parse CONFIG_FILE: %w", err)
		}
	}
	cfg.applyEnv()
	return cfg, nil
}

func (c *Config) applyEnv() {
	overrideInt(&c.Server.GRPCPort, "GRPC_PORT")
	if v, ok := lookupInt("HTTP_PORT"); ok {
		c.Server.HTTPPort = v
		c.Server.WebsocketPort = v
	}
	overrideInt(&c.Server.WebsocketPort, "WEBSOCKET_PORT")
	overrideDuration(&c.Server.ReadTimeout, "READ_TIMEOUT")
	overrideDuration(&c.Server.WriteTimeout, "WRITE_TIMEOUT")
	overrideDuration(&c.Server.IdleTimeout, "IDLE_TIMEOUT")
	overrideInt(&c.Server.MaxConnections, "MAX_CONNECTIONS")
	overrideBool(&c.Server.EnableTLS, "ENABLE_TLS")
	overrideString(&c.Server.TLSCertPath, "TLS_CERT_PATH")
	overrideString(&c.Server.TLSKeyPath, "TLS_KEY_PATH")

	if v, ok := os.LookupEnv("KAFKA_BROKERS"); ok && v != "" {
		c.Kafka.Brokers = splitCSV(v)
	}
	overrideString(&c.Kafka.Topic, "KAFKA_TOPIC")
	overrideString(&c.Kafka.ClientID, "KAFKA_CLIENT_ID")
	overrideString(&c.Kafka.Compression, "KAFKA_COMPRESSION")
	overrideInt(&c.Kafka.BatchSize, "KAFKA_BATCH_SIZE")
	overrideDuration(&c.Kafka.BatchTimeout, "KAFKA_BATCH_TIMEOUT")
	overrideString(&c.Kafka.Acks, "KAFKA_ACKS")
	overrideInt(&c.Kafka.Retries, "KAFKA_RETRIES")
	overrideInt(&c.Kafka.MaxInFlight, "KAFKA_MAX_IN_FLIGHT")
	overrideInt(&c.Kafka.LingerMs, "KAFKA_LINGER_MS")
	overrideInt64(&c.Kafka.BufferMemory, "KAFKA_BUFFER_MEMORY")

	overrideInt(&c.WorkerPool.Shards, "WORKER_POOL_SHARDS")
	overrideInt(&c.WorkerPool.WorkersPerShard, "WORKER_POOL_WORKERS_PER_SHARD")
	overrideInt(&c.WorkerPool.QueueSize, "WORKER_POOL_QUEUE_SIZE")
	overrideInt(&c.WorkerPool.MaxQueueSize, "WORKER_POOL_MAX_QUEUE_SIZE")
	overrideDuration(&c.WorkerPool.Timeout, "WORKER_POOL_TIMEOUT")

	overrideBool(&c.Backpressure.Enabled, "BACKPRESSURE_ENABLED")
	overrideBool(&c.Backpressure.DropOnFull, "BACKPRESSURE_DROP_ON_FULL")
	overrideInt(&c.Backpressure.MaxQueueDepth, "BACKPRESSURE_MAX_QUEUE_DEPTH")
	overrideFloat64(&c.Backpressure.AlertThreshold, "BACKPRESSURE_ALERT_THRESHOLD")
	overrideString(&c.Backpressure.DropStrategy, "BACKPRESSURE_DROP_STRATEGY")

	overrideBool(&c.Metrics.Enabled, "METRICS_ENABLED")
	overrideInt(&c.Metrics.Port, "METRICS_PORT")
	overrideString(&c.Metrics.Path, "METRICS_PATH")
	overrideString(&c.Metrics.PushGatewayURL, "METRICS_PUSH_GATEWAY_URL")

	overrideString(&c.Logging.Level, "LOG_LEVEL")
	overrideString(&c.Logging.Format, "LOG_FORMAT")
	overrideString(&c.Logging.Output, "LOG_OUTPUT")
	overrideString(&c.Logging.FilePath, "LOG_FILE_PATH")

	overrideDuration(&c.Shutdown.Timeout, "SHUTDOWN_TIMEOUT")
	overrideBool(&c.Shutdown.WaitForPending, "SHUTDOWN_WAIT_FOR_PENDING")
	overrideDuration(&c.Shutdown.DrainTimeout, "SHUTDOWN_DRAIN_TIMEOUT")
}

func overrideString(dst *string, key string) {
	if v, ok := os.LookupEnv(key); ok && v != "" {
		*dst = v
	}
}

func overrideInt(dst *int, key string) {
	if v, ok := lookupInt(key); ok {
		*dst = v
	}
}

func overrideInt64(dst *int64, key string) {
	if v, ok := os.LookupEnv(key); ok && v != "" {
		n, err := strconv.ParseInt(v, 10, 64)
		if err == nil {
			*dst = n
		}
	}
}

func overrideFloat64(dst *float64, key string) {
	if v, ok := os.LookupEnv(key); ok && v != "" {
		n, err := strconv.ParseFloat(v, 64)
		if err == nil {
			*dst = n
		}
	}
}

func overrideBool(dst *bool, key string) {
	if v, ok := os.LookupEnv(key); ok && v != "" {
		b, err := strconv.ParseBool(v)
		if err == nil {
			*dst = b
		}
	}
}

func overrideDuration(dst *time.Duration, key string) {
	if v, ok := os.LookupEnv(key); ok && v != "" {
		d, err := time.ParseDuration(v)
		if err == nil {
			*dst = d
		}
	}
}

func lookupInt(key string) (int, bool) {
	v, ok := os.LookupEnv(key)
	if !ok || v == "" {
		return 0, false
	}
	n, err := strconv.Atoi(v)
	if err != nil {
		return 0, false
	}
	return n, true
}

func splitCSV(v string) []string {
	parts := strings.Split(v, ",")
	out := make([]string, 0, len(parts))
	for _, p := range parts {
		p = strings.TrimSpace(p)
		if p != "" {
			out = append(out, p)
		}
	}
	return out
}
