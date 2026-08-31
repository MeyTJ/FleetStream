// Package consumer implements the Kafka consumer with exactly-once processing.
package consumer

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"sync"
	"sync/atomic"
	"time"

	"github.com/IBM/sarama"
	"github.com/fleetstream/streaming-engine/internal/metrics"
	"github.com/fleetstream/streaming-engine/internal/observability"
	"github.com/fleetstream/streaming-engine/pkg/config"
	"github.com/fleetstream/streaming-engine/pkg/kafkasecurity"
	"github.com/sirupsen/logrus"
)

// MessageHandler processes a consumed message
type MessageHandler func(ctx context.Context, msg *sarama.ConsumerMessage) error

// ExactlyOnceConsumer is a Kafka consumer with exactly-once processing semantics
type ExactlyOnceConsumer struct {
	brokers  []string
	topic    string
	groupID  string
	cfg      *config.ConsumerConfig
	logger   *logrus.Logger
	metrics  *metrics.Metrics
	handler  MessageHandler
	client   sarama.ConsumerGroup
	topics   []string
	consumer sarama.ConsumerGroupHandler
	wg       sync.WaitGroup
	inFlight sync.WaitGroup
	sessionActive atomic.Bool
	closed   atomic.Bool
}

// NewExactlyOnceConsumer creates a new exactly-once Kafka consumer
func NewExactlyOnceConsumer(
	brokers []string,
	topic string,
	groupID string,
	cfg *config.ConsumerConfig,
	logger *logrus.Logger,
	m *metrics.Metrics,
	handler MessageHandler,
) (*ExactlyOnceConsumer, error) {
	saramaConfig := sarama.NewConfig()

	// Idempotent consumer settings
	saramaConfig.Consumer.Offsets.Initial = sarama.OffsetNewest
	if cfg.StartOffset == "earliest" {
		saramaConfig.Consumer.Offsets.Initial = sarama.OffsetOldest
	}

	// Performance settings
	saramaConfig.Consumer.MaxWaitTime = cfg.MaxWaitTime
	saramaConfig.Consumer.Group.Session.Timeout = cfg.SessionTimeout
	saramaConfig.Consumer.Group.Rebalance.Timeout = 60 * time.Second
	saramaConfig.Consumer.Group.Heartbeat.Interval = 3 * time.Second
	saramaConfig.Consumer.Return.Errors = true

	// Exactly-once: read only committed messages
	saramaConfig.Consumer.IsolationLevel = sarama.ReadCommitted

	// Manual offset management for exactly-once
	saramaConfig.Consumer.Offsets.AutoCommit.Enable = false

	// Version compatibility
	saramaConfig.Version = sarama.V2_8_0_0
	if err := kafkasecurity.Apply(saramaConfig, kafkasecurity.FromEnv()); err != nil {
		return nil, fmt.Errorf("kafka security: %w", err)
	}

	client, err := sarama.NewConsumerGroup(brokers, groupID, saramaConfig)
	if err != nil {
		return nil, fmt.Errorf("failed to create consumer group: %w", err)
	}

	c := &ExactlyOnceConsumer{
		brokers:  brokers,
		topic:    topic,
		groupID:  groupID,
		cfg:      cfg,
		logger:   logger,
		metrics:  m,
		handler:  handler,
		client:   client,
		topics:   []string{topic},
	}
	c.consumer = &consumerGroupHandler{logger: logger, metrics: m, handler: handler, parent: c}

	return c, nil
}

// IsReady reports whether the consumer has an active group session.
func (c *ExactlyOnceConsumer) IsReady() bool {
	return !c.closed.Load() && c.sessionActive.Load()
}

// WaitForInflight waits for in-flight message handlers to finish.
func (c *ExactlyOnceConsumer) WaitForInflight(timeout time.Duration) {
	done := make(chan struct{})
	go func() {
		c.inFlight.Wait()
		close(done)
	}()

	select {
	case <-done:
	case <-time.After(timeout):
		c.logger.Warn("timed out waiting for in-flight messages")
	}
}

// Start begins consuming messages
func (c *ExactlyOnceConsumer) Start(ctx context.Context) error {
	c.wg.Add(1)

	go func() {
		defer c.wg.Done()

		for {
			if c.closed.Load() {
				return
			}

			if err := c.client.Consume(ctx, c.topics, c.consumer); err != nil {
				if errors.Is(err, sarama.ErrClosedConsumerGroup) {
					return
				}
				c.logger.WithError(err).Error("error from consumer")
				c.metrics.ConsumerErrors.WithLabelValues("consume_loop").Inc()
			}

			if ctx.Err() != nil {
				return
			}
		}
	}()

	// Error handler
	c.wg.Add(1)
	go func() {
		defer c.wg.Done()
		for err := range c.client.Errors() {
			c.logger.WithError(err).Error("consumer error")
			c.metrics.ConsumerErrors.WithLabelValues("kafka").Inc()
		}
	}()

	c.logger.WithField("topic", c.topic).WithField("group", c.groupID).Info("consumer started")
	return nil
}

// Close gracefully closes the consumer
func (c *ExactlyOnceConsumer) Close() error {
	if !c.closed.CompareAndSwap(false, true) {
		return nil
	}

	c.logger.Info("closing consumer")
	return c.client.Close()
}

// WaitForShutdown waits for the consumer to fully shut down
func (c *ExactlyOnceConsumer) WaitForShutdown() {
	c.wg.Wait()
}

// consumerGroupHandler implements sarama.ConsumerGroupHandler
type consumerGroupHandler struct {
	logger  *logrus.Logger
	metrics *metrics.Metrics
	handler MessageHandler
	parent  *ExactlyOnceConsumer
}

// Setup is called when a new session is opened
func (h *consumerGroupHandler) Setup(_ sarama.ConsumerGroupSession) error {
	if h.parent != nil {
		h.parent.sessionActive.Store(true)
	}
	h.logger.Info("consumer group session established")
	h.metrics.ConsumerRebalances.Inc()
	return nil
}

// Cleanup is called when a session is closed
func (h *consumerGroupHandler) Cleanup(_ sarama.ConsumerGroupSession) error {
	if h.parent != nil {
		h.parent.sessionActive.Store(false)
	}
	h.logger.Info("consumer group session ended")
	return nil
}

// ConsumeClaim processes messages from a single claim
func (h *consumerGroupHandler) ConsumeClaim(
	session sarama.ConsumerGroupSession,
	claim sarama.ConsumerGroupClaim,
) error {
	for {
		select {
		case message, ok := <-claim.Messages():
			if !ok {
				h.logger.WithField("topic", claim.Topic()).
					WithField("partition", claim.Partition()).
					Info("message channel closed")
				return nil
			}

			if message == nil {
				continue
			}

			start := time.Now()
			h.metrics.MessagesConsumed.WithLabelValues(message.Topic, fmt.Sprint(message.Partition)).Inc()
			lag := claim.HighWaterMarkOffset() - message.Offset - 1
			if lag < 0 {
				lag = 0
			}
			h.metrics.ConsumerLag.WithLabelValues(message.Topic, fmt.Sprint(message.Partition)).Set(float64(lag))

			correlationID, traceparent := observability.FromKafkaHeaders(message.Headers)
			ctx, cancel := context.WithTimeout(session.Context(), 30*time.Second)
			ctx, _, _ = observability.Ensure(ctx, correlationID, traceparent)

			if h.parent != nil {
				h.parent.inFlight.Add(1)
			}
			err := h.handler(ctx, message)
			if h.parent != nil {
				h.parent.inFlight.Done()
			}
			cancel()

			if err != nil {
				h.logger.WithError(err).
					WithField("topic", message.Topic).
					WithField("partition", message.Partition).
					WithField("offset", message.Offset).
					Error("message processing failed")
				h.metrics.ConsumerErrors.WithLabelValues("processing").Inc()
				continue
			}

			session.MarkMessage(message, "")
			h.metrics.ProcessingDuration.WithLabelValues("consume").Observe(time.Since(start).Seconds())

		case <-session.Context().Done():
			return nil
		}
	}
}

// DeserializeTelemetry deserializes a Kafka message into telemetry
func DeserializeTelemetry(data []byte, v interface{}) error {
	return json.Unmarshal(data, v)
}
