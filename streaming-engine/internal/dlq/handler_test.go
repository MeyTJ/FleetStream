package dlq

import (
	"testing"
	"time"

	"github.com/fleetstream/streaming-engine/internal/metrics"
	"github.com/fleetstream/streaming-engine/pkg/config"
	"github.com/sirupsen/logrus"
)

func TestRecordFailure_RetriesBeforeDLQ(t *testing.T) {
	cfg := &config.DLQConfig{
		Enabled:       true,
		RetryAttempts: 3,
		RetryBackoff:  100 * time.Millisecond,
	}
	logger := logrus.New()
	m := metrics.NewMetrics("fleetstream_streaming_dlq_test_" + t.Name())
	h := &DLQHandler{
		cfg:        cfg,
		logger:     logger,
		metrics:    m,
		retryCount: make(map[string]int),
		retryTimes: make(map[string]time.Time),
	}

	key := []byte("truck-1")
	if !h.RecordFailure(key) {
		t.Fatal("expected first failure to allow retry")
	}
	if !h.RecordFailure(key) {
		t.Fatal("expected second failure to allow retry")
	}
	if h.RecordFailure(key) {
		t.Fatal("expected third failure to exhaust retries")
	}
	if got := h.getRetryCount(key); got != 3 {
		t.Fatalf("retry count = %d, want 3", got)
	}
}

func TestResetRetries_ClearsState(t *testing.T) {
	cfg := &config.DLQConfig{
		Enabled:       true,
		RetryAttempts: 3,
		RetryBackoff:  time.Second,
	}
	h := &DLQHandler{
		cfg:        cfg,
		retryCount: map[string]int{"truck-1": 2},
		retryTimes: map[string]time.Time{"truck-1": time.Now()},
	}

	h.ResetRetries([]byte("truck-1"))
	if len(h.retryCount) != 0 || len(h.retryTimes) != 0 {
		t.Fatal("expected retry state to be cleared")
	}
}
