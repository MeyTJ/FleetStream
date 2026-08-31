// Package metrics provides Prometheus metrics for the streaming engine.
package metrics

import (
	"github.com/prometheus/client_golang/prometheus"
	"github.com/prometheus/client_golang/prometheus/promauto"
)

// Metrics holds all Prometheus metrics for the streaming engine
type Metrics struct {
	MessagesConsumed   *prometheus.CounterVec
	ConsumerErrors     *prometheus.CounterVec
	ConsumerLag        *prometheus.GaugeVec
	ProcessingDuration *prometheus.HistogramVec
	MessagesProcessed  *prometheus.CounterVec
	MessagesDropped    *prometheus.CounterVec
	DuplicatesDropped  *prometheus.CounterVec
	AnomaliesDetected  *prometheus.CounterVec
	MessagesPublished  *prometheus.CounterVec
	PublishErrors      *prometheus.CounterVec
	PublishLatency     *prometheus.HistogramVec
	RedisOperations    *prometheus.CounterVec
	RedisErrors        *prometheus.CounterVec
	DLQMessages        *prometheus.CounterVec
	DLQErrors          prometheus.Counter
	SpeedViolations    prometheus.Counter
	TempAnomalies      prometheus.Counter
	FuelLowEvents      prometheus.Counter
	GeofenceViolations prometheus.Counter
	ActiveWorkers      prometheus.Gauge
	BackpressureEvents prometheus.Counter
	ConsumerRebalances prometheus.Counter
}

// NewMetrics creates and registers all Prometheus metrics
func NewMetrics(namespace string) *Metrics {
	m := &Metrics{
		MessagesConsumed: promauto.NewCounterVec(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "messages_consumed_total",
				Help:      "Total number of messages consumed from Kafka",
			},
			[]string{"topic", "partition"},
		),
		ConsumerErrors: promauto.NewCounterVec(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "consumer_errors_total",
				Help:      "Total number of consumer errors",
			},
			[]string{"type"},
		),
		ConsumerLag: promauto.NewGaugeVec(
			prometheus.GaugeOpts{
				Namespace: namespace,
				Name:      "consumer_lag",
				Help:      "Consumer lag in messages",
			},
			[]string{"topic", "partition"},
		),
		ProcessingDuration: promauto.NewHistogramVec(
			prometheus.HistogramOpts{
				Namespace: namespace,
				Name:      "processing_duration_seconds",
				Help:      "Time to process a single message",
				Buckets:   []float64{0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1},
			},
			[]string{"stage"},
		),
		MessagesProcessed: promauto.NewCounterVec(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "messages_processed_total",
				Help:      "Total number of messages successfully processed",
			},
			[]string{"result"},
		),
		MessagesDropped: promauto.NewCounterVec(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "messages_dropped_total",
				Help:      "Total number of messages dropped",
			},
			[]string{"reason"},
		),
		DuplicatesDropped: promauto.NewCounterVec(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "duplicates_dropped_total",
				Help:      "Total number of duplicate messages dropped",
			},
			[]string{"source"},
		),
		AnomaliesDetected: promauto.NewCounterVec(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "anomalies_detected_total",
				Help:      "Total number of anomalies detected",
			},
			[]string{"type"},
		),
		MessagesPublished: promauto.NewCounterVec(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "messages_published_total",
				Help:      "Total number of messages published",
			},
			[]string{"topic"},
		),
		PublishErrors: promauto.NewCounterVec(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "publish_errors_total",
				Help:      "Total number of publish errors",
			},
			[]string{"reason"},
		),
		PublishLatency: promauto.NewHistogramVec(
			prometheus.HistogramOpts{
				Namespace: namespace,
				Name:      "publish_latency_seconds",
				Help:      "Time to publish a message",
			},
			[]string{"topic"},
		),
		RedisOperations: promauto.NewCounterVec(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "redis_operations_total",
				Help:      "Total number of Redis operations",
			},
			[]string{"operation", "result"},
		),
		RedisErrors: promauto.NewCounterVec(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "redis_errors_total",
				Help:      "Total number of Redis errors",
			},
			[]string{"operation"},
		),
		DLQMessages: promauto.NewCounterVec(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "dlq_messages_total",
				Help:      "Total number of messages sent to DLQ",
			},
			[]string{"reason"},
		),
		DLQErrors: promauto.NewCounter(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "dlq_errors_total",
				Help:      "Total number of DLQ publish errors",
			},
		),
		SpeedViolations: promauto.NewCounter(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "speed_violations_total",
				Help:      "Total number of speed violations",
			},
		),
		TempAnomalies: promauto.NewCounter(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "temp_anomalies_total",
				Help:      "Total number of temperature anomalies",
			},
		),
		FuelLowEvents: promauto.NewCounter(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "fuel_low_events_total",
				Help:      "Total number of low fuel events",
			},
		),
		GeofenceViolations: promauto.NewCounter(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "geofence_violations_total",
				Help:      "Total number of geofence violations",
			},
		),
		ActiveWorkers: promauto.NewGauge(
			prometheus.GaugeOpts{
				Namespace: namespace,
				Name:      "active_workers",
				Help:      "Number of active processing workers",
			},
		),
		BackpressureEvents: promauto.NewCounter(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "backpressure_events_total",
				Help:      "Total number of backpressure events",
			},
		),
		ConsumerRebalances: promauto.NewCounter(
			prometheus.CounterOpts{
				Namespace: namespace,
				Name:      "consumer_rebalances_total",
				Help:      "Total number of consumer group rebalances",
			},
		),
	}

	return m
}
