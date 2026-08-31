package handlers

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/fleetstream/ingress-gateway/internal/processors"
	"github.com/fleetstream/ingress-gateway/pkg/config"
)

type stubKafka struct {
	readyErr error
	stats    processors.ProducerStats
}

func (s stubKafka) Ready(context.Context) error { return s.readyErr }
func (s stubKafka) GetStats() processors.ProducerStats {
	return s.stats
}

type stubPool struct {
	accepting bool
	stats     processors.PoolStats
}

func (s stubPool) Accepting() bool              { return s.accepting }
func (s stubPool) Stats() processors.PoolStats    { return s.stats }

func TestReadyRejectsHighQueueUtilization(t *testing.T) {
	h := NewHealthHandler(
		stubKafka{},
		stubPool{
			accepting: true,
			stats: processors.PoolStats{
				QueueDepth:    9000,
				QueueCapacity: 10000,
			},
		},
		config.BackpressureConfig{
			Enabled:        true,
			AlertThreshold: 0.8,
		},
	)

	req := httptest.NewRequest(http.MethodGet, "/health/ready", nil)
	rec := httptest.NewRecorder()
	h.Ready(rec, req)

	if rec.Code != http.StatusServiceUnavailable {
		t.Fatalf("expected 503, got %d", rec.Code)
	}

	var body readinessBody
	if err := json.NewDecoder(rec.Body).Decode(&body); err != nil {
		t.Fatalf("decode body: %v", err)
	}
	if body.Reason != "worker pool queue depth above threshold" {
		t.Fatalf("unexpected reason: %q", body.Reason)
	}
}

func TestReadyIncludesQueueStatsWhenHealthy(t *testing.T) {
	h := NewHealthHandler(
		stubKafka{stats: processors.ProducerStats{QueueDepth: 10}},
		stubPool{
			accepting: true,
			stats: processors.PoolStats{
				QueueDepth:    100,
				QueueCapacity: 10000,
			},
		},
		config.BackpressureConfig{
			Enabled:        true,
			AlertThreshold: 0.8,
			MaxQueueDepth:  100000,
		},
	)

	req := httptest.NewRequest(http.MethodGet, "/health/ready", nil)
	rec := httptest.NewRecorder()
	h.Ready(rec, req)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected 200, got %d", rec.Code)
	}

	var body readinessBody
	if err := json.NewDecoder(rec.Body).Decode(&body); err != nil {
		t.Fatalf("decode body: %v", err)
	}
	if body.Status != "ready" {
		t.Fatalf("expected ready, got %q", body.Status)
	}
	if body.QueueDepth != 100 || body.QueueCapacity != 10000 {
		t.Fatalf("unexpected queue stats: %+v", body)
	}
}
