package config

import "time"

// Config holds all streaming engine configuration
type Config struct {
	Consumer    ConsumerConfig     `yaml:"consumer"`
	Producer    ProducerConfig     `yaml:"producer"`
	Redis       RedisConfig       `yaml:"redis"`
	Processing  ProcessingConfig  `yaml:"processing"`
	Idempotency IdempotencyConfig `yaml:"idempotency"`
	DLQ         DLQConfig         `yaml:"dlq"`
	Metrics     MetricsConfig     `yaml:"metrics"`
	Shutdown    ShutdownConfig    `yaml:"shutdown"`
}

// ConsumerConfig defines Kafka consumer settings
type ConsumerConfig struct {
	Brokers          []string `yaml:"brokers"`
	Topic            string   `yaml:"topic"`
	GroupID          string   `yaml:"group_id"`
	MaxWaitTime      time.Duration `yaml:"max_wait_time"`
	CommitInterval   time.Duration `yaml:"commit_interval"`
	SessionTimeout   time.Duration `yaml:"session_timeout"`
	StartOffset      string   `yaml:"start_offset"`
	EnableAutoCommit bool     `yaml:"enable_auto_commit"`
}

// ProducerConfig defines Kafka producer settings
type ProducerConfig struct {
	Brokers     []string `yaml:"brokers"`
	Topic       string   `yaml:"topic"`
	Compression string   `yaml:"compression"`
	BatchSize   int      `yaml:"batch_size"`
	Acks        string   `yaml:"acks"`
	Retries     int      `yaml:"retries"`
	Idempotent  bool     `yaml:"idempotent"`
	ExactlyOnce bool     `yaml:"exactly_once"`
}

// RedisConfig defines Redis connection settings
type RedisConfig struct {
	Addresses  []string      `yaml:"addresses"`
	Password   string        `yaml:"password"`
	DB         int           `yaml:"db"`
	PoolSize   int           `yaml:"pool_size"`
	StateTTL   time.Duration `yaml:"state_ttl"`
	DedupTTL   time.Duration `yaml:"dedup_ttl"`
}

// ProcessingConfig defines stream processing settings
type ProcessingConfig struct {
	Concurrency      int            `yaml:"concurrency"`
	BatchSize       int            `yaml:"batch_size"`
	EnrichmentURL   string         `yaml:"enrichment_url"`
	AnomalyThreshold AnomalyConfig `yaml:"anomaly_threshold"`
	RiskScoring     RiskConfig     `yaml:"risk_scoring"`
}

// AnomalyConfig defines anomaly detection thresholds
type AnomalyConfig struct {
	MaxSpeedKmh                 float64 `yaml:"max_speed_kmh"`
	MaxEngineTempCelsius        float64 `yaml:"max_engine_temp_celsius"`
	MinEngineTempCelsius        float64 `yaml:"min_engine_temp_celsius"`
	MinFuelLevelPercent         float32 `yaml:"min_fuel_level_percent"`
	SpeedViolationThresholdKmh  float64 `yaml:"speed_violation_threshold_kmh"`
}

// RiskConfig defines risk scoring parameters
type RiskConfig struct {
	SpeedWeight           float64 `yaml:"speed_weight"`
	TempWeight           float64 `yaml:"temp_weight"`
	FuelWeight           float64 `yaml:"fuel_weight"`
	MediumRiskThreshold  float64 `yaml:"medium_risk_threshold"`
	HighRiskThreshold    float64 `yaml:"high_risk_threshold"`
}

// IdempotencyConfig defines idempotency settings
type IdempotencyConfig struct {
	Enabled        bool          `yaml:"enabled"`
	TTL            time.Duration `yaml:"ttl"`
	DropDuplicates bool          `yaml:"drop_duplicates"`
}

// DLQConfig defines dead letter queue settings
type DLQConfig struct {
	Enabled       bool     `yaml:"enabled"`
	Topic         string   `yaml:"topic"`
	Brokers       []string `yaml:"brokers"`
	RetryAttempts int      `yaml:"retry_attempts"`
}

// MetricsConfig defines metrics and monitoring settings
type MetricsConfig struct {
	Enabled bool   `yaml:"enabled"`
	Port    int    `yaml:"port"`
	Path    string `yaml:"path"`
}

// ShutdownConfig defines graceful shutdown settings
type ShutdownConfig struct {
	Timeout        time.Duration `yaml:"timeout"`
	WaitForPending bool          `yaml:"wait_for_pending"`
	DrainTimeout   time.Duration `yaml:"drain_timeout"`
}

// DefaultConfig returns a default configuration
func DefaultConfig() *Config {
	return &Config{
		Consumer: ConsumerConfig{
			Brokers:         []string{"localhost:9092"},
			Topic:           "fleet.telemetry.raw",
			GroupID:         "fleetstream-streaming-processor",
			MaxWaitTime:     100 * time.Millisecond,
			CommitInterval:  1 * time.Second,
			SessionTimeout:  30 * time.Second,
			StartOffset:     "earliest",
			EnableAutoCommit: false,
		},
		Producer: ProducerConfig{
			Brokers:     []string{"localhost:9092"},
			Topic:       "fleet.telemetry.processed",
			Compression: "snappy",
			BatchSize:   16384,
			Acks:         "all",
			Retries:      3,
			Idempotent:   true,
			ExactlyOnce:  true,
		},
		Redis: RedisConfig{
			Addresses: []string{"localhost:6379"},
			PoolSize:  100,
			StateTTL:  24 * time.Hour,
			DedupTTL:  1 * time.Hour,
		},
		Processing: ProcessingConfig{
			Concurrency: 8,
			BatchSize:   100,
			AnomalyThreshold: AnomalyConfig{
				MaxSpeedKmh:                120,
				MaxEngineTempCelsius:       110,
				MinEngineTempCelsius:       -20,
				MinFuelLevelPercent:        15,
				SpeedViolationThresholdKmh: 100,
			},
			RiskScoring: RiskConfig{
				SpeedWeight:          0.3,
				TempWeight:          0.25,
				FuelWeight:          0.2,
				MediumRiskThreshold: 0.5,
				HighRiskThreshold:   0.75,
			},
		},
		Idempotency: IdempotencyConfig{
			Enabled:        true,
			TTL:            1 * time.Hour,
			DropDuplicates: true,
		},
		DLQ: DLQConfig{
			Enabled:       true,
			Topic:         "fleet.telemetry.dlq",
			Brokers:       []string{"localhost:9092"},
			RetryAttempts: 3,
		},
		Metrics: MetricsConfig{
			Enabled: true,
			Port:    9091,
			Path:    "/metrics",
		},
		Shutdown: ShutdownConfig{
			Timeout:        60 * time.Second,
			WaitForPending: true,
			DrainTimeout:   30 * time.Second,
		},
	}
}
