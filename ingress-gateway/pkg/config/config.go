package config

import (
	"time"
)

// Config holds all application configuration
type Config struct {
	// Server settings
	Server ServerConfig `yaml:"server"`
	
	// Kafka settings
	Kafka KafkaConfig `yaml:"kafka"`
	
	// Worker pool settings
	WorkerPool WorkerPoolConfig `yaml:"worker_pool"`
	
	// Backpressure settings
	Backpressure BackpressureConfig `yaml:"backpressure"`
	
	// Metrics and monitoring
	Metrics MetricsConfig `yaml:"metrics"`
	
	// Logging
	Logging LoggingConfig `yaml:"logging"`
	
	// Graceful shutdown
	Shutdown ShutdownConfig `yaml:"shutdown"`
}

// ServerConfig defines server settings
type ServerConfig struct {
	GRPCPort          int           `yaml:"grpc_port" default:"50051"`
	WebsocketPort     int           `yaml:"websocket_port" default:"8080"`
	HTTPPort          int           `yaml:"http_port" default:"8081"`
	ReadTimeout       time.Duration `yaml:"read_timeout" default:"5s"`
	WriteTimeout      time.Duration `yaml:"write_timeout" default:"10s"`
	MaxConnections    int           `yaml:"max_connections" default:"15000"`
	EnableTLS         bool          `yaml:"enable_tls" default:"false"`
	TLSCertPath       string        `yaml:"tls_cert_path"`
	TLSKeyPath        string        `yaml:"tls_key_path"`
}

// KafkaConfig defines Kafka connection and producer settings
type KafkaConfig struct {
	Brokers           []string     `yaml:"brokers" default:"[\"localhost:9092\"]"`
	Topic             string       `yaml:"topic" default:"fleet.telemetry.raw"`
	ClientID          string       `yaml:"client_id" default:"ingress-gateway"`
	Compression       string       `yaml:"compression" default:"snappy"`
	BatchSize         int          `yaml:"batch_size" default:"16384"`
	BatchTimeout      time.Duration `yaml:"batch_timeout" default:"10ms"`
	Acks              string       `yaml:"acks" default:"all"`
	Retries           int          `yaml:"retries" default:"3"`
	MaxInFlight       int          `yaml:"max_in_flight" default:"5"`
	LingerMs          int          `yaml:"linger_ms" default:"5"`
	BufferMemory      int64        `yaml:"buffer_memory" default:"33554432"`
}

// WorkerPoolConfig defines sharded worker pool settings
type WorkerPoolConfig struct {
	Shards            int           `yaml:"shards" default:"8"`
	WorkersPerShard   int           `yaml:"workers_per_shard" default:"4"`
	QueueSize         int           `yaml:"queue_size" default:"10000"`
	MaxQueueSize      int           `yaml:"max_queue_size" default:"100000"`
	Timeout           time.Duration `yaml:"timeout" default:"30s"`
}

// BackpressureConfig defines backpressure handling settings
type BackpressureConfig struct {
	Enabled         bool          `yaml:"enabled" default:"true"`
	DropOnFull      bool          `yaml:"drop_on_full" default:"true"`
	MaxQueueDepth   int           `yaml:"max_queue_depth" default:"100000"`
	AlertThreshold  float64       `yaml:"alert_threshold" default:"0.8"` // 80% full
	DropStrategy    string        `yaml:"drop_strategy" default:"oldest"` // oldest, newest, random
}

// MetricsConfig defines metrics and monitoring settings
type MetricsConfig struct {
	Enabled          bool          `yaml:"enabled" default:"true"`
	Port             int           `yaml:"port" default:"9090"`
	Path             string        `yaml:"path" default:"/metrics"`
	Labels           map[string]string `yaml:"labels"`
	PushGatewayURL   string        `yaml:"push_gateway_url"`
}

// LoggingConfig defines logging settings
type LoggingConfig struct {
	Level             string `yaml:"level" default:"info"`
	Format            string `yaml:"format" default:"json"`
	Output            string `yaml:"output" default:"stdout"`
	FilePath          string `yaml:"file_path"`
	MaxSize           int    `yaml:"max_size" default:"100"` // MB
	MaxBackups        int    `yaml:"max_backups" default:"5"`
	MaxAge            int    `yaml:"max_age" default:"30"`    // days
}

// ShutdownConfig defines graceful shutdown settings
type ShutdownConfig struct {
	Timeout         time.Duration `yaml:"timeout" default:"30s"`
	WaitForPending  bool          `yaml:"wait_for_pending" default:"true"`
	DrainTimeout    time.Duration `yaml:"drain_timeout" default:"10s"`
}

func DefaultConfig() *Config {
	return &Config{}
}
