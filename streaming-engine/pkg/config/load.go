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
	if v, ok := os.LookupEnv("KAFKA_BROKERS"); ok && v != "" {
		brokers := splitCSV(v)
		c.Consumer.Brokers = brokers
		c.Producer.Brokers = brokers
		c.DLQ.Brokers = brokers
	}
	if v, ok := os.LookupEnv("CONSUMER_BROKERS"); ok && v != "" {
		c.Consumer.Brokers = splitCSV(v)
	}
	overrideString(&c.Consumer.Topic, "CONSUMER_TOPIC")
	overrideString(&c.Consumer.GroupID, "CONSUMER_GROUP_ID")
	overrideDuration(&c.Consumer.MaxWaitTime, "CONSUMER_MAX_WAIT_TIME")
	overrideDuration(&c.Consumer.CommitInterval, "CONSUMER_COMMIT_INTERVAL")
	overrideDuration(&c.Consumer.SessionTimeout, "CONSUMER_SESSION_TIMEOUT")
	overrideString(&c.Consumer.StartOffset, "CONSUMER_START_OFFSET")
	overrideBool(&c.Consumer.EnableAutoCommit, "CONSUMER_ENABLE_AUTO_COMMIT")

	if v, ok := os.LookupEnv("PRODUCER_BROKERS"); ok && v != "" {
		c.Producer.Brokers = splitCSV(v)
	}
	overrideString(&c.Producer.Topic, "PRODUCER_TOPIC")
	overrideString(&c.Producer.Compression, "PRODUCER_COMPRESSION")
	overrideInt(&c.Producer.BatchSize, "PRODUCER_BATCH_SIZE")
	overrideString(&c.Producer.Acks, "PRODUCER_ACKS")
	overrideInt(&c.Producer.Retries, "PRODUCER_RETRIES")
	overrideBool(&c.Producer.Idempotent, "PRODUCER_IDEMPOTENT")
	overrideBool(&c.Producer.ExactlyOnce, "PRODUCER_EXACTLY_ONCE")

	if v, ok := os.LookupEnv("REDIS_ADDRESSES"); ok && v != "" {
		c.Redis.Addresses = splitCSV(v)
	} else if v, ok := os.LookupEnv("REDIS_ADDR"); ok && v != "" {
		c.Redis.Addresses = splitCSV(v)
	}
	overrideString(&c.Redis.Password, "REDIS_PASSWORD")
	overrideInt(&c.Redis.DB, "REDIS_DB")
	overrideInt(&c.Redis.PoolSize, "REDIS_POOL_SIZE")
	overrideDuration(&c.Redis.StateTTL, "REDIS_STATE_TTL")
	overrideDuration(&c.Redis.DedupTTL, "REDIS_DEDUP_TTL")
	overrideBool(&c.Redis.Cluster, "REDIS_CLUSTER")

	overrideInt(&c.Processing.Concurrency, "PROCESSING_CONCURRENCY")
	overrideInt(&c.Processing.BatchSize, "PROCESSING_BATCH_SIZE")
	overrideString(&c.Processing.EnrichmentURL, "PROCESSING_ENRICHMENT_URL")
	overrideFloat64(&c.Processing.AnomalyThreshold.MaxSpeedKmh, "ANOMALY_MAX_SPEED_KMH")
	overrideFloat64(&c.Processing.AnomalyThreshold.MaxEngineTempCelsius, "ANOMALY_MAX_ENGINE_TEMP_CELSIUS")
	overrideFloat64(&c.Processing.AnomalyThreshold.MinEngineTempCelsius, "ANOMALY_MIN_ENGINE_TEMP_CELSIUS")
	overrideFloat32(&c.Processing.AnomalyThreshold.MinFuelLevelPercent, "ANOMALY_MIN_FUEL_LEVEL_PERCENT")
	overrideFloat64(&c.Processing.AnomalyThreshold.SpeedViolationThresholdKmh, "ANOMALY_SPEED_VIOLATION_THRESHOLD_KMH")
	overrideFloat64(&c.Processing.RiskScoring.SpeedWeight, "RISK_SPEED_WEIGHT")
	overrideFloat64(&c.Processing.RiskScoring.TempWeight, "RISK_TEMP_WEIGHT")
	overrideFloat64(&c.Processing.RiskScoring.FuelWeight, "RISK_FUEL_WEIGHT")
	overrideFloat64(&c.Processing.RiskScoring.MediumRiskThreshold, "RISK_MEDIUM_THRESHOLD")
	overrideFloat64(&c.Processing.RiskScoring.HighRiskThreshold, "RISK_HIGH_THRESHOLD")

	overrideBool(&c.Idempotency.Enabled, "IDEMPOTENCY_ENABLED")
	overrideDuration(&c.Idempotency.TTL, "IDEMPOTENCY_TTL")
	overrideBool(&c.Idempotency.DropDuplicates, "IDEMPOTENCY_DROP_DUPLICATES")

	if v, ok := os.LookupEnv("DLQ_BROKERS"); ok && v != "" {
		c.DLQ.Brokers = splitCSV(v)
	}
	overrideBool(&c.DLQ.Enabled, "DLQ_ENABLED")
	overrideString(&c.DLQ.Topic, "DLQ_TOPIC")
	overrideInt(&c.DLQ.RetryAttempts, "DLQ_RETRY_ATTEMPTS")
	overrideDuration(&c.DLQ.RetryBackoff, "DLQ_RETRY_BACKOFF")

	overrideBool(&c.Metrics.Enabled, "METRICS_ENABLED")
	overrideInt(&c.Metrics.Port, "METRICS_PORT")
	overrideString(&c.Metrics.Path, "METRICS_PATH")

	overrideInt(&c.Admin.Port, "ADMIN_PORT")

	overrideString(&c.Logging.Level, "LOG_LEVEL")

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

func overrideFloat64(dst *float64, key string) {
	if v, ok := os.LookupEnv(key); ok && v != "" {
		n, err := strconv.ParseFloat(v, 64)
		if err == nil {
			*dst = n
		}
	}
}

func overrideFloat32(dst *float32, key string) {
	if v, ok := os.LookupEnv(key); ok && v != "" {
		n, err := strconv.ParseFloat(v, 32)
		if err == nil {
			*dst = float32(n)
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
