package engine

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"testing"
	"time"

	"github.com/IBM/sarama"
	"github.com/fleetstream/streaming-engine/internal/metrics"
	"github.com/fleetstream/streaming-engine/internal/processor"
	"github.com/fleetstream/streaming-engine/pkg/config"
	"github.com/fleetstream/streaming-engine/pkg/models"
	"github.com/sirupsen/logrus"
)

type stubState struct {
	dup       bool
	dupErr    error
	getErr    error
	setErr    error
	markErr   error
	marked    bool
	setCalled bool
	prev      *models.TruckState
	markCalls int
}

func (s *stubState) CheckDuplicate(context.Context, string) (bool, error) {
	return s.dup, s.dupErr
}
func (s *stubState) MarkProcessed(context.Context, string) error {
	s.markCalls++
	s.marked = true
	return s.markErr
}
func (s *stubState) GetTruckState(context.Context, string) (*models.TruckState, error) {
	return s.prev, s.getErr
}
func (s *stubState) SetTruckState(context.Context, *models.TruckState) error {
	s.setCalled = true
	return s.setErr
}

type stubPublisher struct {
	err   error
	calls int
}

func (p *stubPublisher) Publish(context.Context, string, string, []byte) error {
	p.calls++
	return p.err
}

type stubDLQ struct {
	calls int
	last  error
}

func (d *stubDLQ) SendToDLQ(_ context.Context, _ *sarama.ConsumerMessage, err error) error {
	d.calls++
	d.last = err
	return nil
}

func validPayload() []byte {
	b, _ := json.Marshal(models.TelemetryPayload{
		TruckID:                  "truck-1",
		MessageID:                "msg-1",
		Timestamp:                time.Now(),
		Latitude:                 40.7,
		Longitude:                -74.0,
		EngineTemperatureCelsius: 90,
		SpeedKmh:                 60,
		FuelLevelPercent:         50,
	})
	return b
}

func newTestHandler(t *testing.T, state *stubState, pub *stubPublisher, dlq *stubDLQ) *Handler {
	t.Helper()
	cfg := config.DefaultConfig()
	logger := logrus.New()
	logger.SetOutput(io.Discard)
	m := metrics.NewMetrics("fleetstream_streaming_engine_test_" + t.Name())
	return NewHandler(cfg, logger, m, state, pub, dlq, processor.NewStreamProcessor(&cfg.Processing, logger, m))
}

func TestHandle_RedisDown_SendsDLQAndReturnsError(t *testing.T) {
	state := &stubState{getErr: errors.New("redis down")}
	pub := &stubPublisher{}
	dlq := &stubDLQ{}
	h := newTestHandler(t, state, pub, dlq)

	err := h.Handle(context.Background(), &sarama.ConsumerMessage{Value: validPayload()})
	if err == nil {
		t.Fatal("expected error")
	}
	if pub.calls != 0 {
		t.Fatalf("publish calls = %d, want 0", pub.calls)
	}
	if dlq.calls != 1 {
		t.Fatalf("dlq calls = %d, want 1", dlq.calls)
	}
	if state.marked {
		t.Fatal("must not mark processed on redis failure")
	}
}

func TestHandle_PublishFailure_SendsDLQAndDoesNotMark(t *testing.T) {
	state := &stubState{}
	pub := &stubPublisher{err: errors.New("kafka unavailable")}
	dlq := &stubDLQ{}
	h := newTestHandler(t, state, pub, dlq)

	err := h.Handle(context.Background(), &sarama.ConsumerMessage{Value: validPayload()})
	if err == nil {
		t.Fatal("expected error")
	}
	if pub.calls != 1 {
		t.Fatalf("publish calls = %d, want 1", pub.calls)
	}
	if dlq.calls != 1 {
		t.Fatalf("dlq calls = %d, want 1", dlq.calls)
	}
	if state.marked {
		t.Fatal("must not mark processed on publish failure")
	}
}

func TestHandle_Success_PublishesAndMarks(t *testing.T) {
	state := &stubState{}
	pub := &stubPublisher{}
	dlq := &stubDLQ{}
	h := newTestHandler(t, state, pub, dlq)

	if err := h.Handle(context.Background(), &sarama.ConsumerMessage{Value: validPayload()}); err != nil {
		t.Fatal(err)
	}
	if pub.calls != 1 {
		t.Fatalf("publish calls = %d, want 1", pub.calls)
	}
	if dlq.calls != 0 {
		t.Fatalf("dlq calls = %d, want 0", dlq.calls)
	}
	if !state.setCalled || !state.marked {
		t.Fatal("expected state update and mark processed")
	}
}

func TestHandle_DuplicateDropped(t *testing.T) {
	state := &stubState{dup: true}
	pub := &stubPublisher{}
	dlq := &stubDLQ{}
	h := newTestHandler(t, state, pub, dlq)

	if err := h.Handle(context.Background(), &sarama.ConsumerMessage{Value: validPayload()}); err != nil {
		t.Fatal(err)
	}
	if pub.calls != 0 {
		t.Fatalf("publish calls = %d, want 0", pub.calls)
	}
}
