// Package processors provides metrics collection and monitoring.
package processors

import (
	"github.com/prometheus/client_golang/prometheus"
	"github.com/prometheus/client_golang/prometheus/promauto"
)

// Metrics holds all Prometheus metrics for the ingress gateway
type Metrics struct {
	// Job metrics
	JobsEnqueued      *prometheus.CounterVec
	JobsProcessed     *prometheus.CounterVec
	JobsDropped       *prometheus.CounterVec
	ProcessingDuration *prometheus.HistogramVec
	
	// Queue metrics
	ShardQueueDepth   *prometheus.GaugeVec
	PoolQueueCapacity prometheus.Gauge
	
	// Connection metrics
	ActiveGRPCConnections    prometheus.Gauge
	ActiveWebsocketConnections prometheus.Gauge
	ActiveHTTPConnections     prometheus.Gauge
	
	// Kafka metrics
	KafkaMessagesPublished prometheus.Counter
	KafkaPublishErrors   prometheus.Counter
	KafkaQueueDepth      prometheus.Gauge
	
	// Backpressure metrics
	BackpressureState prometheus.Gauge
	BackpressureDrops prometheus.Counter
}

// NewMetrics creates and registers all Prometheus metrics
func NewMetrics(namespace string) *Metrics {
	m := &Metrics{
		JobsEnqueued: promauto.NewCounterVec(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "jobs_enqueued_total",
				Help:      "Total number of jobs enqueued",
			},
			[]string{"source"},
		),
		JobsProcessed: promauto.NewCounterVec(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "jobs_processed_total",
				Help:      "Total number of jobs processed",
			},
			[]string{"source"},
		),
		JobsDropped: promauto.NewCounterVec(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "jobs_dropped_total",
				Help:      "Total number of jobs dropped due to backpressure",
			},
			[]string{"source", "reason"},
		),
		ProcessingDuration: promauto.NewHistogramVec(
			prometheus.HistogramOpts{
				Namespace: namespace,
				Name:      "job_processing_duration_seconds",
				Help:      "Duration of job processing",
				Buckets:   prometheus.DefBuckets,
			},
			[]string{"source"},
		),
		ShardQueueDepth: promauto.NewGaugeVec(
			prometheus.GaugeOpts{
				Namespace: namespace,
				Name:      "shard_queue_depth",
				Help:      "Current depth of each shard queue",
			},
			[]string{"shard"},
		),
		PoolQueueCapacity: promauto.NewGauge(
			prometheus.GaugeOpts{
				Namespace: namespace,
				Name:      "pool_queue_capacity",
				Help:      "Total queue capacity of the pool",
			},
		),
		ActiveGRPCConnections: promauto.NewGauge(
			prometheus.GaugeOpts{
				Namespace: namespace,
				Name:      "active_grpc_connections",
				Help:      "Number of active gRPC connections",
			},
		),
		ActiveWebsocketConnections: promauto.NewGauge(
			prometheus.GaugeOpts{
				Namespace: namespace,
				Name:      "active_websocket_connections",
				Help:      "Number of active WebSocket connections",
			},
		),
		ActiveHTTPConnections: promauto.NewGauge(
			prometheus.GaugeOpts{
				Namespace: namespace,
				Name:      "active_http_connections",
				Help:      "Number of active HTTP connections",
			},
		),
		KafkaMessagesPublished: promauto.NewCounter(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "kafka_messages_published_total",
				Help:      "Total number of messages published to Kafka",
			},
		),
		KafkaPublishErrors: promauto.NewCounter(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "kafka_publish_errors_total",
				Help:      "Total number of Kafka publish errors",
			},
		),
		KafkaQueueDepth: promauto.NewGauge(
			prometheus.GaugeOpts{
				Namespace: namespace,
				Name:      "kafka_queue_depth",
				Help:      "Current depth of Kafka producer queue",
			},
		),
		BackpressureState: promauto.NewGauge(
			prometheus.GaugeOpts{
				Namespace: namespace,
				Name:      "backpressure_state",
				Help:      "Current backpressure state (0=normal, 1=warning, 2=critical)",
			},
		),
		BackpressureDrops: promauto.NewCounter(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "backpressure_drops_total",
				Help:      "Total number of messages dropped due to backpressure",
			},
		),
	}

	return m
}
