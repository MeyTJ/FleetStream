package consumer

import (
	"context"
	"errors"
	"sync"
	"testing"
	"time"

	"github.com/IBM/sarama"
	"github.com/fleetstream/streaming-engine/internal/metrics"
	"github.com/sirupsen/logrus"
)

type mockSession struct {
	ctx    context.Context
	mu     sync.Mutex
	marked []*sarama.ConsumerMessage
}

func (m *mockSession) Claims() map[string][]int32               { return nil }
func (m *mockSession) MemberID() string                         { return "m" }
func (m *mockSession) GenerationID() int32                      { return 1 }
func (m *mockSession) MarkOffset(string, int32, int64, string)  {}
func (m *mockSession) Commit()                                  {}
func (m *mockSession) ResetOffset(string, int32, int64, string) {}
func (m *mockSession) MarkMessage(msg *sarama.ConsumerMessage, _ string) {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.marked = append(m.marked, msg)
}
func (m *mockSession) Context() context.Context { return m.ctx }

type mockClaim struct {
	ch chan *sarama.ConsumerMessage
}

func (c *mockClaim) Topic() string                            { return "fleet.telemetry.raw" }
func (c *mockClaim) Partition() int32                         { return 0 }
func (c *mockClaim) InitialOffset() int64                     { return 0 }
func (c *mockClaim) HighWaterMarkOffset() int64               { return 0 }
func (c *mockClaim) Messages() <-chan *sarama.ConsumerMessage { return c.ch }

func runClaim(t *testing.T, handler MessageHandler, msg *sarama.ConsumerMessage) *mockSession {
	t.Helper()
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	session := &mockSession{ctx: ctx}
	ch := make(chan *sarama.ConsumerMessage, 1)
	ch <- msg
	close(ch)
	h := &consumerGroupHandler{
		logger:  logrus.New(),
		metrics: metrics.NewMetrics("fleetstream_streaming_consumer_test_" + t.Name()),
		handler: handler,
	}
	if err := h.ConsumeClaim(session, &mockClaim{ch: ch}); err != nil {
		t.Fatal(err)
	}
	return session
}

func TestConsumeClaim_DoesNotMarkOnHandlerError(t *testing.T) {
	msg := &sarama.ConsumerMessage{Topic: "t", Partition: 0, Offset: 7, Value: []byte(`{}`)}
	session := runClaim(t, func(context.Context, *sarama.ConsumerMessage) error {
		return errors.New("process failed")
	}, msg)
	if len(session.marked) != 0 {
		t.Fatalf("marked %d, want 0", len(session.marked))
	}
}

func TestConsumeClaim_MarksOnSuccess(t *testing.T) {
	msg := &sarama.ConsumerMessage{Topic: "t", Partition: 0, Offset: 8, Value: []byte(`{}`)}
	session := runClaim(t, func(context.Context, *sarama.ConsumerMessage) error {
		return nil
	}, msg)
	if len(session.marked) != 1 {
		t.Fatalf("marked %d, want 1", len(session.marked))
	}
}

func TestConsumeClaim_ContextDone(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	session := &mockSession{ctx: ctx}
	ch := make(chan *sarama.ConsumerMessage)
	h := &consumerGroupHandler{
		logger:  logrus.New(),
		metrics: metrics.NewMetrics("fleetstream_streaming_consumer_test_ctx"),
		handler: func(context.Context, *sarama.ConsumerMessage) error { return nil },
	}
	done := make(chan error, 1)
	go func() { done <- h.ConsumeClaim(session, &mockClaim{ch: ch}) }()
	select {
	case err := <-done:
		if err != nil {
			t.Fatal(err)
		}
	case <-time.After(2 * time.Second):
		t.Fatal("ConsumeClaim did not return")
	}
}
