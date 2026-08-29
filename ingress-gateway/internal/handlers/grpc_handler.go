// Package handlers implements the gRPC and WebSocket handlers for telemetry ingestion.
package handlers

import (
	"context"
	"encoding/json"
	"net/http"
	"sync"
	"time"

	"github.com/gorilla/websocket"
	"github.com/fleetstream/ingress-gateway/internal/processors"
	"github.com/fleetstream/ingress-gateway/pkg/models"
	"github.com/sirupsen/logrus"
)

// Upgrader for WebSocket connections
var upgrader = websocket.Upgrader{
	ReadBufferSize:  1024,
	WriteBufferSize: 1024,
	CheckOrigin: func(r *http.Request) bool {
		return true
	},
}

// GRPCHandler handles gRPC telemetry ingestion
type GRPCHandler struct {
	pool    *processors.ShardedPool
	metrics *processors.Metrics
	logger  *logrus.Logger
}

// NewGRPCHandler creates a new gRPC handler
func NewGRPCHandler(pool *processors.ShardedPool, metrics *processors.Metrics, logger *logrus.Logger) *GRPCHandler {
	return &GRPCHandler{
		pool:    pool,
		metrics: metrics,
		logger:  logger,
	}
}

// Ingest handles unary telemetry ingestion via gRPC
func (h *GRPCHandler) Ingest(ctx context.Context, payload models.TelemetryPayload) (*models.IngestResult, error) {
	start := time.Now()
	
	// Validate payload
	if err := validatePayload(payload); err != nil {
		h.logger.WithError(err).Warn("invalid payload received")
		return &models.IngestResult{
			Accepted: false,
			Reason:   err.Error(),
		}, nil
	}

	// Submit to worker pool
	err := h.pool.Submit(ctx, payload, "grpc")
	result := &models.IngestResult{
		MessageID:   payload.MessageID,
		ProcessedAt: time.Now().UnixMilli(),
	}

	if err != nil {
		result.Accepted = false
		result.Rejected = true
		result.Reason = err.Error()
		if h.metrics != nil {
			h.metrics.JobsDropped.WithLabelValues("grpc", err.Error()).Inc()
		}
	} else {
		result.Accepted = true
		if h.metrics != nil {
			h.metrics.ProcessingDuration.WithLabelValues("grpc").Observe(time.Since(start).Seconds())
		}
	}

	return result, nil
}
