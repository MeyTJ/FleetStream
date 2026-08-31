package handlers

import (
	"context"
	"encoding/json"
	"net/http"

	"github.com/fleetstream/ingress-gateway/internal/processors"
	"github.com/fleetstream/ingress-gateway/pkg/config"
)

type KafkaHealth interface {
	Ready(ctx context.Context) error
	GetStats() processors.ProducerStats
}

type PoolReadiness interface {
	Accepting() bool
	Stats() processors.PoolStats
}

type HealthHandler struct {
	kafka        KafkaHealth
	pool         PoolReadiness
	backpressure config.BackpressureConfig
}

func NewHealthHandler(kafka KafkaHealth, pool PoolReadiness, backpressure config.BackpressureConfig) *HealthHandler {
	return &HealthHandler{
		kafka:        kafka,
		pool:         pool,
		backpressure: backpressure,
	}
}

func (h *HealthHandler) Live(w http.ResponseWriter, _ *http.Request) {
	writeReadiness(w, http.StatusOK, readinessBody{Status: "live"})
}

func (h *HealthHandler) Ready(w http.ResponseWriter, r *http.Request) {
	if h.pool != nil {
		if !h.pool.Accepting() {
			writeReadiness(w, http.StatusServiceUnavailable, readinessBody{
				Status: "not ready",
				Reason: "worker pool not accepting jobs",
			})
			return
		}

		if h.backpressure.Enabled {
			stats := h.pool.Stats()
			if util := queueUtilization(stats.QueueDepth, stats.QueueCapacity); util >= h.backpressure.AlertThreshold {
				writeReadiness(w, http.StatusServiceUnavailable, readinessBody{
					Status:           "not ready",
					Reason:           "worker pool queue depth above threshold",
					QueueDepth:       stats.QueueDepth,
					QueueCapacity:    stats.QueueCapacity,
					QueueUtilization: util,
				})
				return
			}
		}
	}

	if h.kafka != nil && h.backpressure.Enabled {
		stats := h.kafka.GetStats()
		if util := queueUtilization(int(stats.QueueDepth), h.backpressure.MaxQueueDepth); util >= h.backpressure.AlertThreshold {
			writeReadiness(w, http.StatusServiceUnavailable, readinessBody{
				Status:              "not ready",
				Reason:              "kafka producer backpressure above threshold",
				KafkaQueueDepth:     int(stats.QueueDepth),
				KafkaMaxQueueDepth:  h.backpressure.MaxQueueDepth,
				KafkaQueueUtilization: util,
			})
			return
		}
	}

	if h.kafka != nil {
		if err := h.kafka.Ready(r.Context()); err != nil {
			writeReadiness(w, http.StatusServiceUnavailable, readinessBody{
				Status: "not ready",
				Reason: "kafka broker unreachable",
			})
			return
		}
	}

	body := readinessBody{Status: "ready"}
	if h.pool != nil {
		stats := h.pool.Stats()
		body.QueueDepth = stats.QueueDepth
		body.QueueCapacity = stats.QueueCapacity
		body.QueueUtilization = queueUtilization(stats.QueueDepth, stats.QueueCapacity)
	}
	if h.kafka != nil {
		kafkaStats := h.kafka.GetStats()
		body.KafkaQueueDepth = int(kafkaStats.QueueDepth)
		body.KafkaMaxQueueDepth = h.backpressure.MaxQueueDepth
		body.KafkaQueueUtilization = queueUtilization(int(kafkaStats.QueueDepth), h.backpressure.MaxQueueDepth)
	}
	writeReadiness(w, http.StatusOK, body)
}

type readinessBody struct {
	Status                string  `json:"status"`
	Reason                string  `json:"reason,omitempty"`
	QueueDepth            int     `json:"queue_depth,omitempty"`
	QueueCapacity         int     `json:"queue_capacity,omitempty"`
	QueueUtilization      float64 `json:"queue_utilization,omitempty"`
	KafkaQueueDepth       int     `json:"kafka_queue_depth,omitempty"`
	KafkaMaxQueueDepth    int     `json:"kafka_max_queue_depth,omitempty"`
	KafkaQueueUtilization float64 `json:"kafka_queue_utilization,omitempty"`
}

func queueUtilization(depth, capacity int) float64 {
	if capacity <= 0 {
		return 0
	}
	return float64(depth) / float64(capacity)
}

func writeReadiness(w http.ResponseWriter, status int, body readinessBody) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(body)
}